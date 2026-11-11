using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Iot.Device.Authentication;
using Microsoft.Azure.Iot.Device.Models;
using Microsoft.Azure.Iot.Device.MqttNetAdapter;
using Microsoft.Azure.Iot.Device.MQTTnetAdapter;
using Microsoft.Azure.Iot.Device.Provisioning.Models;
using Microsoft.Azure.Iot.Device.Unified.Connection;
using Microsoft.Azure.Iot.Device.Unified.Models;
using Microsoft.Azure.Iot.Device.Unified.Telemetry;

namespace AzureDpsFramework;

// Retained for existing package consumers; the sorter uses the SDK clients directly.
public sealed class UnifiedDeviceSession : IDisposable
{
    private readonly DeviceProvisioningIdentity identity;
    private readonly CoordinatedMqttClient mqtt;
    public AuthenticationProvider Authentication => identity.Authentication;
    public ProvisioningSettings Provisioning => identity.Provisioning;
    public ConnectionClient Connection { get; }
    public TelemetryClient Telemetry { get; }
    public ConnectionContext? Context { get; private set; }

    private UnifiedDeviceSession(DeviceProvisioningIdentity identity, CoordinatedMqttClient mqtt,
        ConnectionClient connection, TelemetryClient telemetry)
    {
        this.identity = identity;
        this.mqtt = mqtt;
        Connection = connection;
        Telemetry = telemetry;
    }

    public static UnifiedDeviceSession Create(DpsConfiguration configuration)
    {
        var identity = DeviceProvisioningIdentity.Create(configuration);
        CoordinatedMqttClient? mqtt = null;
        ConnectionClient? connection = null;
        try
        {
            mqtt = new CoordinatedMqttClient(new MqttNetClient(new MqttNetClientOptions
            {
                EnableMqttLogs = configuration.EnableDebugLogging
            }), configuration.ProvisioningHost, configuration.ProvisioningLockPath);
            connection = new ConnectionClient(new ConnectionClientOptions
            {
                MqttClient = mqtt,
                EnableMqttLogging = configuration.EnableDebugLogging,
                EnableTwin = false,
                EnableDirectMethods = false,
                EnableCloudToDeviceMessages = false
            }, identity.LoadContext());
            return new UnifiedDeviceSession(identity, mqtt, connection, new TelemetryClient(connection));
        }
        catch
        {
            connection?.Dispose();
            mqtt?.Dispose();
            identity.Dispose();
            throw;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connecting = Connection.ProvisionAndConnectAsync(Provisioning, Authentication, cancellationToken: connectCancellation.Token);
        if (await Task.WhenAny(connecting, identity.CredentialFailure) == identity.CredentialFailure)
            connectCancellation.Cancel();
        try { Context = await connecting; }
        catch (Exception ex) when (identity.CredentialFailure.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(identity.CredentialFailure.Result.Message, identity.CredentialFailure.Result.InnerException ?? ex);
        }
        catch (Exception)
        {
            var context = Connection.GetCurrentConnectionContext();
            Console.Error.WriteLine($"[IoT] Connection failed. Assigned Hub={context?.IotHubHostName ?? "(none)"}, profile={context?.ConnectionProfile}, issued certificate count={context?.IssuedClientCertificates?.Count ?? 0}.");
            throw;
        }
        identity.SaveContext(Context);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        Connection.DisconnectAsync(cancellationToken: cancellationToken);

    public void Dispose()
    {
        Telemetry.Dispose();
        Connection.Dispose();
        mqtt.Dispose();
        identity.Dispose();
    }
}
