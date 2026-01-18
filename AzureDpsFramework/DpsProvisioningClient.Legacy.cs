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
        public string[]? issuedCertificateChain { get; set; }  // Array of base64-encoded certificates
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
            // CRITICAL: For group enrollments, derive device-specific key from group primary key
            var enrollmentGroupKey = _cfg.EnrollmentGroupKeyBase64 ?? throw new InvalidOperationException("EnrollmentGroupKeyBase64 is required");
            var derivedDeviceKey = DpsSasTokenGenerator.DeriveDeviceKey(_cfg.RegistrationId, enrollmentGroupKey);
            var sas = DpsSasTokenGenerator.GenerateDpsSas(_cfg.IdScope, _cfg.RegistrationId, derivedDeviceKey, _cfg.SasExpirySeconds);

            var factory = new MqttFactory();
            using var client = factory.CreateMqttClient();
            
            // URL-encode the ClientVersion user agent
            var userAgent = Uri.EscapeDataString("skittlesorter-csharp/1.0.0");
            // Try using PREVIEW API version when CSR is involved
            var apiVersion = "2025-07-01-preview";  // Use preview API for CSR support
            var username = $"{_cfg.IdScope}/registrations/{_cfg.RegistrationId}/api-version={apiVersion}&ClientVersion={userAgent}";
            Console.WriteLine($"\n[MQTT] MQTT Connection Details:");
            Console.WriteLine($"[MQTT] Host: {_cfg.ProvisioningHost}:{_cfg.MqttPort}");
            Console.WriteLine($"[MQTT] Username: {username}");
            Console.WriteLine($"[MQTT] Password (SAS Token) length: {sas.Length} chars");
            Console.WriteLine($"[MQTT] Password (first 60 chars): {sas.Substring(0, Math.Min(60, sas.Length))}...");
            Console.WriteLine($"[MQTT] ClientId: {_cfg.RegistrationId}");
            Console.WriteLine($"[MQTT] Attempting connection...\n");
            
            var opts = new MqttClientOptionsBuilder()
                .WithTcpServer(_cfg.ProvisioningHost, _cfg.MqttPort)
                .WithTlsOptions(_ => { })  // Enable TLS with defaults
                .WithCredentials(username, sas)
                .WithClientId(_cfg.RegistrationId)
                .WithCleanSession()
                .Build();

            var tcs = new TaskCompletionSource<DpsResponse>();
            string requestId = Guid.NewGuid().ToString();
            Console.WriteLine($"[MQTT] Request ID: {requestId}");

            client.ApplicationMessageReceivedAsync += e =>
            {
                try
                {
                    var topic = e.ApplicationMessage.Topic;
                    Console.WriteLine($"[MQTT] Received message on topic: {topic}");
                    
                    if (topic.StartsWith("$dps/registrations/res/", StringComparison.OrdinalIgnoreCase) && topic.Contains(requestId))
                    {
                        var statusCodePart = topic.Substring("$dps/registrations/res/".Length);
                        var statusCode = statusCodePart.Split('/')[0];
                        Console.WriteLine($"[MQTT] Status code from topic: {statusCode}");
                        
                        // Check for 401 Unauthorized - stop the app
                        if (statusCode == "401")
                        {
                            var payloadArray = e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>();
                            var payloadJson = Encoding.UTF8.GetString(payloadArray.AsSpan(e.ApplicationMessage.PayloadSegment.Offset, e.ApplicationMessage.PayloadSegment.Count));
                            Console.WriteLine($"[MQTT ERROR] ❌ 401 Unauthorized from DPS");
                            Console.WriteLine($"[MQTT ERROR] Response: {payloadJson}");
                            tcs.TrySetException(new InvalidOperationException("DPS authentication failed: 401 Unauthorized. Check enrollment group key and credentials."));
                            return Task.CompletedTask;
                        }
                        
                        var payloadArray2 = e.ApplicationMessage.PayloadSegment.Array ?? Array.Empty<byte>();
                        var payloadJson2 = Encoding.UTF8.GetString(payloadArray2.AsSpan(e.ApplicationMessage.PayloadSegment.Offset, e.ApplicationMessage.PayloadSegment.Count));
                        Console.WriteLine($"[MQTT] Response payload: {payloadJson2}");
                        
                        var resp = JsonConvert.DeserializeObject<DpsResponse>(payloadJson2) ?? new DpsResponse();
                        Console.WriteLine($"[MQTT] Parsed response - status: {resp.status}, operationId: {resp.operationId}");
                        
                        // Pass through; consumer will handle polling if needed
                        tcs.TrySetResult(resp);
                    }
                    else
                    {
                        Console.WriteLine($"[MQTT] Topic doesn't match expected pattern or requestId");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MQTT ERROR] Failed to parse response: {ex.Message}");
                    // ignore parsing errors
                }
                return Task.CompletedTask;
            };

            try
            {
                Console.WriteLine("[MQTT] Attempting MQTT connection...");
                await client.ConnectAsync(opts, ct);
                Console.WriteLine("[MQTT] ✅ Connected successfully to DPS!\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT ERROR] ❌ Connection failed: {ex.GetType().Name}");
                Console.WriteLine($"[MQTT ERROR] Message: {ex.Message}");
                throw new InvalidOperationException("Failed to connect to DPS MQTT broker", ex);
            }

            // Subscribe to all DPS responses
            Console.WriteLine("[MQTT] Subscribing to: $dps/registrations/res/#");
            await client.SubscribeAsync("$dps/registrations/res/#");
            Console.WriteLine("[MQTT] Subscription complete\n");

            // Publish initial registration with CSR for certificate issuance
            // DPS expects CSR as base64-encoded DER
            string csrBase64 = GenerateCsrBase64(csrPem);
            
            // Try different payload structures:
            // First attempt: just registrationId and csr (no extra fields)
            var registrationPayload = new
            {
                registrationId = _cfg.RegistrationId,
                csr = csrBase64
            };
            string payload = JsonConvert.SerializeObject(registrationPayload);
            string registerTopic = $"$dps/registrations/PUT/iotdps-register/?$rid={requestId}";
            
            Console.WriteLine($"[MQTT] Publishing registration to: {registerTopic}");
            Console.WriteLine($"[MQTT] Payload length: {payload.Length} bytes");
            Console.WriteLine($"[MQTT] Payload (first 200 chars): {payload.Substring(0, Math.Min(200, payload.Length))}...");
            
            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(registerTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(appMsg, ct);
            Console.WriteLine("[MQTT] Registration request published. Waiting for response (30s timeout)...\n");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            
            try
            {
                var initial = await tcs.Task.WaitAsync(cts.Token);
                Console.WriteLine($"[MQTT] Received initial response - status: {initial.status}");
                
                // If assigning, poll until assigned
                if (string.Equals(initial.status, "assigning", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(initial.operationId))
                {
                    Console.WriteLine($"[MQTT] Device is 'assigning'. Starting polling with operationId: {initial.operationId}");
                    // simple polling loop with 2s delay default
                    for (int i = 0; i < 20; i++)
                    {
                        Console.WriteLine($"[MQTT] Polling attempt {i + 1}/20...");
                        
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
            catch (TimeoutException)
            {
                Console.WriteLine("[MQTT ERROR] ❌ Timeout waiting for DPS response (30 seconds)");
                throw new TimeoutException("DPS registration timed out after 30 seconds");
            }
        }

        private static string GenerateCsrBase64(string csrPem)
        {
            // Extract DER from PEM CSR
            var lines = csrPem.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var derLines = new System.Collections.Generic.List<string>();
            bool inPem = false;
            
            foreach (var line in lines)
            {
                if (line.Contains("BEGIN CERTIFICATE REQUEST"))
                {
                    inPem = true;
                    continue;
                }
                if (line.Contains("END CERTIFICATE REQUEST"))
                {
                    inPem = false;
                    continue;
                }
                if (inPem)
                {
                    derLines.Add(line);
                }
            }
            
            return string.Join("", derLines);
        }
    }
}
