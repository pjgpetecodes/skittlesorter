using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace AzureSoftwareUpdatesFramework;

public sealed class DpsMqttSoftwareUpdatesChannel : IDpsSoftwareUpdatesChannel
{
    private readonly SoftwareUpdatesConfiguration _configuration;
    private readonly AgentInformation _agentInfo;
    private readonly UpdateStateStore _stateStore;
    private readonly IMqttClient _client;
    private readonly X509Certificate2 _deviceCertificate;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DpsResponse>> _pending = new();
    private readonly TimeSpan _timeout;
    private long _nextRequestId;
    private bool _disposed;

    public DpsMqttSoftwareUpdatesChannel(
        SoftwareUpdatesConfiguration configuration,
        AgentInformation agentInfo,
        UpdateStateStore stateStore)
    {
        _configuration = configuration;
        _agentInfo = agentInfo;
        _stateStore = stateStore;
        _timeout = TimeSpan.FromSeconds(configuration.RequestTimeoutSeconds);
        _deviceCertificate = X509Certificate2.CreateFromPemFile(
            configuration.CertificatePath, configuration.PrivateKeyPath!);
        if (!_deviceCertificate.HasPrivateKey)
            throw new InvalidOperationException("The configured DPS X.509 certificate has no private key.");

        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += args =>
        {
            foreach (var request in _pending.Values)
                request.TrySetException(new IOException("The DPS MQTT session disconnected."));
            return Task.CompletedTask;
        };
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var username =
            $"{_configuration.IdScope}/registrations/{_configuration.RegistrationId}/api-version={SoftwareUpdatesProtocol.ApiVersion}";
        var options = new MqttClientOptionsBuilder()
            .WithClientId(_configuration.RegistrationId)
            .WithTcpServer(_configuration.GlobalEndpoint, 8883)
            .WithCredentials(username)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .WithTlsOptions(tls => tls
                .UseTls()
                .WithClientCertificates(new[] { _deviceCertificate }))
            .Build();

        MqttClientConnectResult result;
        try
        {
            result = await _client.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (MQTTnet.Exceptions.MqttCommunicationException exception)
        {
            throw new IOException("DPS MQTT connection could not be established.", exception);
        }
        if (result.ResultCode != MqttClientConnectResultCode.Success)
            throw new IOException($"DPS MQTT connection failed: {result.ResultCode}.");
        try
        {
            await _client.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic(SoftwareUpdatesProtocol.ResponseSubscription)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (MQTTnet.Exceptions.MqttCommunicationException exception)
        {
            throw new IOException("DPS MQTT subscription failed; the session must be reopened.", exception);
        }
    }

    public async Task<UpdateCheckResponse> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = _stateStore.State;
            var body = SoftwareUpdatesProtocol.GetUpdateRequest(
                _agentInfo,
                state.InstalledUpdateId,
                state.AgentInfoEtag,
                state.ServiceConfigEtag);
            var response = await SendAsync(
                SoftwareUpdatesProtocol.GetUpdateOperation,
                body,
                cancellationToken).ConfigureAwait(false);
            ThrowIfError(response, isReport: false);

            using var document = JsonDocument.Parse(response.Payload);
            var root = document.RootElement;
            var agentEtag = OptionalString(root, "agentInfoEtag");
            var serviceEtag = OptionalString(root, "serviceConfigEtag");
            OfferedUpdate? offered = null;
            if (TryGetUnique(root, "updateMetadata", out var metadata))
            {
                if (metadata.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("DPS updateMetadata must be an object.");
                var workflowId = RequiredString(metadata, "workflowId");
                var manifest = RequiredString(metadata, "updateManifest");
                var signature = RequiredString(metadata, "updateManifestSignature");
                var urlsElement = RequiredObject(metadata, "fileUrls");
                var urls = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in urlsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String ||
                        !urls.TryAdd(property.Name, property.Value.GetString()!))
                        throw new InvalidDataException("DPS returned malformed or duplicate fileUrls.");
                }
                offered = new OfferedUpdate(workflowId, manifest, signature, urls);
            }

            _stateStore.State.AgentInfoEtag = agentEtag;
            _stateStore.State.ServiceConfigEtag = serviceEtag;
            await _stateStore.SaveAsync(cancellationToken).ConfigureAwait(false);
            return new UpdateCheckResponse(agentEtag, serviceEtag, offered);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ReportAsync(InstallReport report, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await SendAsync(
                SoftwareUpdatesProtocol.ReportStatusOperation,
                SoftwareUpdatesProtocol.ReportRequest(report),
                cancellationToken).ConfigureAwait(false);
            ThrowIfError(response, isReport: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<DpsResponse> SendAsync(
        string operation,
        string body,
        CancellationToken cancellationToken)
    {
        if (!_client.IsConnected)
            throw new IOException("DPS MQTT session is not connected.");

        var requestId = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<DpsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("Could not register DPS request correlation.");

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(SoftwareUpdatesProtocol.RequestTopic(operation, requestId))
                .WithPayload(body)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            try
            {
                await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (MQTTnet.Exceptions.MqttCommunicationException exception)
            {
                throw new IOException("DPS MQTT publication failed; the session must be reopened.", exception);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"DPS did not answer correlated {operation} request {requestId} within {_timeout}.");
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        if (!DpsResponseTopicParser.TryParse(args.ApplicationMessage.Topic, out var parsed) || parsed is null)
            return Task.CompletedTask;
        if (!_pending.TryGetValue(parsed.RequestId, out var completion))
            return Task.CompletedTask;
        var payload = args.ApplicationMessage.PayloadSegment.ToArray();
        completion.TrySetResult(new DpsResponse(parsed.StatusCode, parsed.RequestId, payload, parsed.RetryAfter));
        return Task.CompletedTask;
    }

    private static void ThrowIfError(DpsResponse response, bool isReport)
    {
        if (response.StatusCode is >= 200 and < 300)
            return;

        int? errorCode = null;
        string? serviceCode = null;
        string? trackingId = null;
        var message = $"DPS update operation returned status {response.StatusCode}.";
        try
        {
            using var body = JsonDocument.Parse(response.Payload);
            if (TryGetUnique(body.RootElement, "errorCode", out var code) &&
                code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var numeric))
                errorCode = numeric;
            if (TryGetUnique(body.RootElement, "message", out var text) && text.ValueKind == JsonValueKind.String)
            {
                message = text.GetString()!;
                serviceCode = message;
            }
            if (TryGetUnique(body.RootElement, "trackingId", out var tracking) &&
                tracking.ValueKind == JsonValueKind.String)
                trackingId = tracking.GetString();
            if (TryGetUnique(body.RootElement, "info", out var info) &&
                TryGetUnique(info, "aduErrorCode", out var aduCode) && aduCode.ValueKind == JsonValueKind.String)
                serviceCode = aduCode.GetString();
        }
        catch (JsonException)
        {
            // The current DPS contract classifies an empty/unparseable error body by HTTP status.
        }

        if (isReport && response.StatusCode == 409)
            message = $"DPS refused the report with a conflict; it is not treated as an acknowledgement. {message}";
        throw new DpsOperationException(
            response.StatusCode, errorCode, serviceCode, response.RequestId, trackingId, response.RetryAfter, message);
    }

    private static string? OptionalString(JsonElement parent, string name)
    {
        if (!TryGetUnique(parent, name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!TryGetUnique(parent, name, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"DPS response field '{name}' is missing or invalid.");
        return value.GetString()!;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!TryGetUnique(parent, name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"DPS response field '{name}' is missing or invalid.");
        return value;
    }

    private static bool TryGetUnique(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        if (parent.ValueKind != JsonValueKind.Object)
            return false;
        var matches = 0;
        foreach (var property in parent.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                matches++;
                value = property.Value;
            }
        }
        if (matches > 1)
            throw new InvalidDataException($"DPS response field '{name}' is duplicated.");
        return matches == 1;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_client.IsConnected)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _client.DisconnectAsync(
                    new MqttClientDisconnectOptionsBuilder().Build(), timeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _client.Dispose();
            _deviceCertificate.Dispose();
            _operationGate.Dispose();
        }
    }

    private sealed record DpsResponse(int StatusCode, string RequestId, byte[] Payload, TimeSpan? RetryAfter);
}
