using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;

namespace AzureDpsFramework
{
    public class DpsRegistrationState
    {
        public string? deviceId { get; set; }
        public string? assignedHub { get; set; }
        public string? substatus { get; set; }
        public string? issuedCertificateChain { get; set; }
    }

    public class DpsResponse
    {
        public string? operationId { get; set; }
        public string? status { get; set; }
        public DpsRegistrationState? registrationState { get; set; }
    }

    public class DpsProvisioningClient
    {
        private readonly DpsConfiguration _cfg;
        public DpsProvisioningClient(DpsConfiguration cfg) { _cfg = cfg; }

        public async Task<DpsResponse> RegisterWithCsrAsync(string csrPem, CancellationToken ct)
        {
            var deviceKeyBase64 = _cfg.GetDeviceKeyOrDerive();
            var sas = DpsSasTokenGenerator.GenerateDpsSas(_cfg.IdScope, _cfg.RegistrationId, deviceKeyBase64, _cfg.SasExpirySeconds);

            var factory = new MqttFactory();
            using var client = factory.CreateMqttClient();
            
            var opts = new MqttClientOptionsBuilder()
                .WithTcpServer(_cfg.ProvisioningHost, _cfg.MqttPort)
                .WithCredentials($"{_cfg.IdScope}/registrations/{_cfg.RegistrationId}/api-version={_cfg.ApiVersion}&ClientVersion=skittlesorter-csharp/1.0.0", sas)
                .WithClientId(_cfg.RegistrationId)
                .WithCleanSession()
                .Build();

            var tcs = new TaskCompletionSource<DpsResponse>();
            string requestId = Guid.NewGuid().ToString();

            client.ApplicationMessageReceivedAsync += e =>
            {
                try
                {
                    var topic = e.ApplicationMessage.Topic;
                    if (topic.StartsWith("$dps/registrations/res/", StringComparison.OrdinalIgnoreCase) && topic.Contains(requestId))
                    {
                        var statusCodePart = topic.Substring("$dps/registrations/res/".Length);
                        var statusCode = statusCodePart.Split('/')[0];
                        var payloadArray = e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>();
                        var payloadJson = Encoding.UTF8.GetString(payloadArray.AsSpan(e.ApplicationMessage.PayloadSegment.Offset, e.ApplicationMessage.PayloadSegment.Count));
                        var resp = JsonConvert.DeserializeObject<DpsResponse>(payloadJson) ?? new DpsResponse();
                        // Pass through; consumer will handle polling if needed
                        tcs.TrySetResult(resp);
                    }
                }
                catch
                {
                    // ignore parsing errors
                }
                return Task.CompletedTask;
            };

            await client.ConnectAsync(opts, ct);

            // Subscribe to all DPS responses
            await client.SubscribeAsync("$dps/registrations/res/#");

            // Publish initial registration with CSR
            var registrationPayload = new
            {
                registrationId = _cfg.RegistrationId,
                csr = csrPem,
                payload = (object?)null
            };
            string payload = JsonConvert.SerializeObject(registrationPayload);
            string registerTopic = $"$dps/registrations/PUT/iotdps-register/?$rid={requestId}";
            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(registerTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(appMsg, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var initial = await tcs.Task.WaitAsync(cts.Token);

            // If assigning, poll until assigned
            if (string.Equals(initial.status, "assigning", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(initial.operationId))
            {
                // simple polling loop with 2s delay default
                for (int i = 0; i < 20; i++)
                {
                    string pollTopic = $"$dps/registrations/GET/iotdps-get-operationstatus/?$rid={requestId}&operationId={initial.operationId}";
                    var pollPayload = new { operationId = initial.operationId, registrationId = _cfg.RegistrationId };
                    string pollJson = JsonConvert.SerializeObject(pollPayload);
                    var pollMsg = new MqttApplicationMessageBuilder().WithTopic(pollTopic).WithPayload(pollJson).Build();
                    await client.PublishAsync(pollMsg, ct);

                    var tcsPoll = new TaskCompletionSource<DpsResponse>();
                    client.ApplicationMessageReceivedAsync += e =>
                    {
                        try
                        {
                            var topic = e.ApplicationMessage.Topic;
                            if (topic.StartsWith("$dps/registrations/res/", StringComparison.OrdinalIgnoreCase) && topic.Contains(requestId))
                            {
                                var payloadArray = e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>();
                                var payloadJson = Encoding.UTF8.GetString(payloadArray.AsSpan(e.ApplicationMessage.PayloadSegment.Offset, e.ApplicationMessage.PayloadSegment.Count));
                                var resp = JsonConvert.DeserializeObject<DpsResponse>(payloadJson) ?? new DpsResponse();
                                tcsPoll.TrySetResult(resp);
                            }
                        }
                        catch { }
                        return Task.CompletedTask;
                    };

                    using var pollCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var polled = await tcsPoll.Task.WaitAsync(pollCts.Token);
                    if (string.Equals(polled.status, "assigned", StringComparison.OrdinalIgnoreCase))
                    {
                        await client.DisconnectAsync();
                        return polled;
                    }
                    if (string.Equals(polled.status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        await client.DisconnectAsync();
                        return polled;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }

            await client.DisconnectAsync();
            return initial;
        }
    }
}
