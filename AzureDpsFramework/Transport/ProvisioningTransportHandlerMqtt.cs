using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;

namespace AzureDpsFramework.Transport
{
    /// <summary>
    /// MQTT transport handler for device provisioning.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client.Transport.ProvisioningTransportHandlerMqtt pattern.
    /// </summary>
    public class ProvisioningTransportHandlerMqtt : ProvisioningTransportHandler
    {
        private readonly string _provisioningHost;
        private readonly int _port;
        private readonly bool _enableDebugLogging;
        private bool _disposed;

        /// <summary>
        /// Creates a new ProvisioningTransportHandlerMqtt with default settings.
        /// </summary>
        /// <param name="provisioningHost">The DPS endpoint hostname (default: global.azure-devices-provisioning.net).</param>
        /// <param name="port">The MQTT port (default: 8883).</param>
        /// <param name="enableDebugLogging">Enable verbose debug logging for MQTT operations.</param>
        public ProvisioningTransportHandlerMqtt(
            string provisioningHost = "global.azure-devices-provisioning.net",
            int port = 8883,
            bool enableDebugLogging = false)
        {
            _provisioningHost = provisioningHost;
            _port = port;
            _enableDebugLogging = enableDebugLogging;
        }

        public override async Task<DeviceRegistrationResult> RegisterAsync(
            ProvisioningTransportRegisterMessage message,
            CancellationToken cancellationToken)
        {
            var idScope = message.IdScope;
            var csrPem = message.CsrPem;
            var sasToken = message.SasToken;
            var securityProvider = message.Security;

            if (securityProvider == null)
                throw new ArgumentException("Security provider is required", nameof(message));

            var registrationId = securityProvider.GetRegistrationID();

            // Determine authentication method based on security provider type
            bool useX509Auth = securityProvider is Security.SecurityProviderX509;
            Security.SecurityProviderX509? x509Provider = useX509Auth ? (Security.SecurityProviderX509)securityProvider : null;

            var factory = new MqttFactory();
            using var client = factory.CreateMqttClient();

            // URL-encode the ClientVersion user agent
            var userAgent = Uri.EscapeDataString("AzureDpsFramework/1.0.0");
            
            // PREVIEW: Use 2025-07-01-preview API when CSR is provided for certificate issuance
            // Standard DPS operations use the 2019-03-31 API version
            var apiVersion = !string.IsNullOrWhiteSpace(csrPem) ? "2025-07-01-preview" : "2019-03-31";
            var username = $"{idScope}/registrations/{registrationId}/api-version={apiVersion}&ClientVersion={userAgent}";

            if (_enableDebugLogging)
            {
                Console.WriteLine($"\n[MQTT] MQTT Connection Details:");
                Console.WriteLine($"[MQTT] Host: {_provisioningHost}:{_port}");
                Console.WriteLine($"[MQTT] Username: {username}");
                Console.WriteLine($"[MQTT] Authentication: {(useX509Auth ? "X.509 Client Certificate" : "SAS Token")}");
                if (!useX509Auth)
                    Console.WriteLine($"[MQTT] Password (SAS Token) length: {sasToken?.Length ?? 0} chars");
                Console.WriteLine($"[MQTT] ClientId: {registrationId}");
                Console.WriteLine($"[MQTT] Attempting connection...");
            }

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_provisioningHost, _port)
                .WithClientId(registrationId)
                .WithCleanSession();

            // Configure authentication based on provider type
            if (useX509Auth)
            {
                // X.509 client certificate authentication
                var authCert = x509Provider!.GetAuthenticationCertificate();
                var certChain = x509Provider.GetAuthenticationCertificateChain();
                
                var clientCerts = new System.Collections.Generic.List<System.Security.Cryptography.X509Certificates.X509Certificate2> { authCert };
                if (certChain != null)
                {
                    foreach (var cert in certChain)
                    {
                        clientCerts.Add(cert);
                    }
                }

                optionsBuilder
                    .WithTlsOptions(o =>
                    {
                        o.UseTls();
                        o.WithClientCertificates(clientCerts);
                    })
                    .WithCredentials(username, string.Empty); // No password for X.509 auth
            }
            else
            {
                // SAS token authentication
                if (string.IsNullOrWhiteSpace(sasToken))
                    throw new ArgumentException("SAS token is required for symmetric key authentication", nameof(message));

                optionsBuilder
                    .WithTlsOptions(o => o.UseTls())
                    .WithCredentials(username, sasToken);
            }

            var opts = optionsBuilder.Build();

            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<DpsResponse>();

            client.ApplicationMessageReceivedAsync += args =>
            {
                try
                {
                    var topic = args.ApplicationMessage.Topic;
                    var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

                    if (_enableDebugLogging)
                    {
                        Console.WriteLine($"[MQTT] Received message on topic: {topic}");
                    }
                    
                    // Extract status code from topic
                    if (topic.Contains("$dps/registrations/res/"))
                    {
                        var statusStr = topic.Split('/')[3].Split('?')[0];
                        if (_enableDebugLogging)
                        {
                            Console.WriteLine($"[MQTT] Status code from topic: {statusStr}");
                            Console.WriteLine($"[MQTT] Response payload: {payload}");
                        }

                        var response = JsonConvert.DeserializeObject<DpsResponse>(payload);
                        if (response != null)
                        {
                            if (_enableDebugLogging)
                            {
                                Console.WriteLine($"[MQTT] Parsed response - status: {response.status}, operationId: {response.operationId}");
                            }
                            tcs.TrySetResult(response);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MQTT ERROR] Failed to parse response: {ex.Message}");
                }
                return Task.CompletedTask;
            };

            try
            {
                if (_enableDebugLogging)
                {
                    Console.WriteLine("[MQTT] Attempting MQTT connection...");
                }
                await client.ConnectAsync(opts, cancellationToken);
                Console.WriteLine("[MQTT] ✅ Connected successfully to DPS!\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT ERROR] ❌ Connection failed: {ex.GetType().Name}");
                Console.WriteLine($"[MQTT ERROR] Message: {ex.Message}");
                throw new InvalidOperationException("Failed to connect to DPS MQTT broker", ex);
            }

            // Subscribe to all DPS responses
            if (_enableDebugLogging)
            {
                Console.WriteLine("[MQTT] Subscribing to: $dps/registrations/res/#");
            }
            await client.SubscribeAsync("$dps/registrations/res/#");
            if (_enableDebugLogging)
            {
                Console.WriteLine("[MQTT] Subscription complete\n");
            }

            // Build registration payload
            object registrationPayload;
            if (!string.IsNullOrWhiteSpace(csrPem))
            {
                // PREVIEW: Include CSR in registration payload for certificate issuance
                // The 2025-07-01-preview API accepts a "csr" field containing base64-encoded DER
                // DPS will issue a certificate chain and return it in the registration response
                var csrBase64 = GenerateCsrBase64(csrPem);
                registrationPayload = new
                {
                    registrationId = registrationId,
                    csr = csrBase64  // NEW: CSR for certificate issuance via ADR
                };
            }
            else
            {
                // Standard registration payload (no CSR)
                registrationPayload = new { registrationId = registrationId };
            }

            string payload = JsonConvert.SerializeObject(registrationPayload);
            string registerTopic = $"$dps/registrations/PUT/iotdps-register/?$rid={requestId}";

            if (_enableDebugLogging)
            {
                Console.WriteLine($"[MQTT] Publishing registration to: {registerTopic}");
                Console.WriteLine($"[MQTT] Payload length: {payload.Length} bytes");
                Console.WriteLine($"[MQTT] Payload (first 200 chars): {payload.Substring(0, Math.Min(200, payload.Length))}...");
            }

            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(registerTopic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await client.PublishAsync(appMsg, cancellationToken);
            if (_enableDebugLogging)
            {
                Console.WriteLine("[MQTT] Registration request published. Waiting for response (30s timeout)...\n");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var initial = await tcs.Task.WaitAsync(cts.Token);
                if (_enableDebugLogging)
                {
                    Console.WriteLine($"[MQTT] Received initial response - status: {initial.status}");
                }

                // If assigning, poll until assigned
                if (string.Equals(initial.status, "assigning", StringComparison.OrdinalIgnoreCase) && 
                    !string.IsNullOrWhiteSpace(initial.operationId))
                {
                    if (_enableDebugLogging)
                    {
                        Console.WriteLine($"[MQTT] Device is 'assigning'. Starting polling with operationId: {initial.operationId}");
                    }
                    
                    for (int i = 0; i < 20; i++)
                    {
                        if (_enableDebugLogging)
                        {
                            Console.WriteLine($"[MQTT] Polling attempt {i + 1}/20...");
                        }

                        var tcsPoll = new TaskCompletionSource<DpsResponse>();
                        
                        // Temporarily replace handler for polling
                        client.ApplicationMessageReceivedAsync += args =>
                        {
                            try
                            {
                                var topic = args.ApplicationMessage.Topic;
                                var pollPayload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);

                                if (topic.Contains("$dps/registrations/res/"))
                                {
                                    var statusStr = topic.Split('/')[3].Split('?')[0];
                                    if (_enableDebugLogging)
                                    {
                                        Console.WriteLine($"[MQTT] Status code from topic: {statusStr}");
                                        Console.WriteLine($"[MQTT] Response payload: {pollPayload}");
                                    }

                                    var response = JsonConvert.DeserializeObject<DpsResponse>(pollPayload);
                                    if (response != null)
                                    {
                                        if (_enableDebugLogging)
                                        {
                                            Console.WriteLine($"[MQTT] Parsed response - status: {response.status}, operationId: {response.operationId}");
                                        }
                                        tcsPoll.TrySetResult(response);
                                    }
                                }
                            }
                            catch { }
                            return Task.CompletedTask;
                        };

                        string pollTopic = $"$dps/registrations/GET/iotdps-get-operationstatus/?$rid={requestId}&operationId={initial.operationId}";
                        var pollPayloadObj = new { operationId = initial.operationId, registrationId = registrationId };
                        string pollJson = JsonConvert.SerializeObject(pollPayloadObj);
                        var pollMsg = new MqttApplicationMessageBuilder().WithTopic(pollTopic).WithPayload(pollJson).Build();

                        await client.PublishAsync(pollMsg, cancellationToken);
                        await Task.Delay(2000, cancellationToken);

                        var pollResult = await tcsPoll.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                        
                        if (pollResult.status == "assigned")
                        {
                            initial = pollResult;
                            break;
                        }
                    }
                }

                await client.DisconnectAsync();

                // Convert to DeviceRegistrationResult
                return ConvertToDeviceRegistrationResult(initial);
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

        private static DeviceRegistrationResult ConvertToDeviceRegistrationResult(DpsResponse response)
        {
            var status = response.status?.ToLowerInvariant() switch
            {
                "assigned" => ProvisioningRegistrationStatusType.Assigned,
                "assigning" => ProvisioningRegistrationStatusType.Assigning,
                "failed" => ProvisioningRegistrationStatusType.Failed,
                "disabled" => ProvisioningRegistrationStatusType.Disabled,
                _ => ProvisioningRegistrationStatusType.Unassigned
            };

            return new DeviceRegistrationResult(
                registrationId: response.registrationState?.registrationId,
                deviceId: response.registrationState?.deviceId,
                assignedHub: response.registrationState?.assignedHub,
                status: status,
                substatus: response.registrationState?.substatus,
                issuedCertificateChain: response.registrationState?.issuedCertificateChain
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Cleanup MQTT resources if needed
            }

            _disposed = true;
        }

        private string ExtractRegistrationIdFromSasToken(string sasToken)
        {
            // SAS token format: SharedAccessSignature sr={url-encoded-resource-uri}&sig={signature}&se={expiry}&skn=registration
            // Resource URI format: {idScope}/registrations/{registrationId}
            // Extract registrationId from sr parameter
            var srStart = sasToken.IndexOf("sr=", StringComparison.Ordinal);
            if (srStart < 0)
                throw new ArgumentException("Invalid SAS token format: missing 'sr' parameter");

            var srValueStart = srStart + 3;
            var srValueEnd = sasToken.IndexOf('&', srValueStart);
            var srValue = srValueEnd < 0 
                ? sasToken.Substring(srValueStart) 
                : sasToken.Substring(srValueStart, srValueEnd - srValueStart);

            // Decode and extract last segment
            var decoded = Uri.UnescapeDataString(srValue);
            var lastSlash = decoded.LastIndexOf('/');
            if (lastSlash < 0)
                throw new ArgumentException("Invalid SAS token format: cannot extract registrationId from resource URI");

            return decoded.Substring(lastSlash + 1);
        }

        // Internal response types for MQTT parsing
        private class DpsResponse
        {
            public string? operationId { get; set; }
            public string? status { get; set; }
            public DpsRegistrationState? registrationState { get; set; }
        }

        private class DpsRegistrationState
        {
            public string? registrationId { get; set; }
            public string? deviceId { get; set; }
            public string? assignedHub { get; set; }
            public string? substatus { get; set; }
            public string[]? issuedCertificateChain { get; set; }
        }
    }
}
