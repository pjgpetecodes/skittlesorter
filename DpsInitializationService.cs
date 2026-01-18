using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Azure.Devices.Client;
using AzureDpsFramework;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;

namespace skittle_sorter
{
    public class DpsInitializationService
    {
        public static DeviceClient? Initialize(IoTHubConfig iotConfig)
        {
            try
            {
                var dpsCfg = DpsConfiguration.Load();

                // Auto-generate CSR + key if enabled and files missing
                if (dpsCfg.AutoGenerateCsr && (!File.Exists(dpsCfg.CsrFilePath) || !File.Exists(dpsCfg.CsrKeyFilePath)))
                {
                    Console.WriteLine("Generating CSR and private key…");
                    var (csrPem, keyPem) = CertificateManager.GenerateCsr(dpsCfg.RegistrationId);
                    CertificateManager.SaveText(dpsCfg.CsrFilePath, csrPem);
                    CertificateManager.SaveText(dpsCfg.CsrKeyFilePath, keyPem);
                }

                var csrPemText = File.ReadAllText(dpsCfg.CsrFilePath);
                var privateKeyPem = File.ReadAllText(dpsCfg.CsrKeyFilePath);
                
                Console.WriteLine("\n=== DPS Configuration ===\n");
                Console.WriteLine($"IdScope: {dpsCfg.IdScope}");
                Console.WriteLine($"RegistrationId: {dpsCfg.RegistrationId}");
                Console.WriteLine($"ProvisioningHost: {dpsCfg.ProvisioningHost}");
                Console.WriteLine($"MqttPort: {dpsCfg.MqttPort}");
                Console.WriteLine($"EnrollmentGroupKeyBase64 (first 30 chars): {dpsCfg.EnrollmentGroupKeyBase64?.Substring(0, Math.Min(30, dpsCfg.EnrollmentGroupKeyBase64.Length))}...");
                Console.WriteLine($"ApiVersion: {dpsCfg.ApiVersion}");
                Console.WriteLine($"SasExpirySeconds: {dpsCfg.SasExpirySeconds}");
                Console.WriteLine($"AutoGenerateCsr: {dpsCfg.AutoGenerateCsr}");
                Console.WriteLine("\n=== Starting DPS Registration ===\n");
                
                // Create security provider with CSR and enrollment group key
                // This matches Microsoft's SecurityProvider pattern
                using var security = SecurityProviderX509Csr.CreateFromEnrollmentGroup(
                    dpsCfg.RegistrationId,
                    csrPemText,
                    privateKeyPem,
                    dpsCfg.EnrollmentGroupKeyBase64);

                // Create transport handler - matches Microsoft's ProvisioningTransportHandler pattern
                using var transport = new ProvisioningTransportHandlerMqtt(
                    dpsCfg.ProvisioningHost,
                    dpsCfg.MqttPort);

                // Create provisioning client using factory pattern - matches Microsoft's ProvisioningDeviceClient.Create()
                using var provisioningClient = ProvisioningDeviceClient.Create(
                    dpsCfg.ProvisioningHost,
                    dpsCfg.IdScope,
                    security,
                    transport);

                Console.WriteLine("Using symmetric-key authentication (DPS)");
                Console.WriteLine($"Connecting to {dpsCfg.ProvisioningHost}:{dpsCfg.MqttPort}...");

                // Register the device - matches Microsoft's RegisterAsync() pattern
                var result = provisioningClient.RegisterAsync(CancellationToken.None).GetAwaiter().GetResult();
                
                Console.WriteLine($"\nDPS Response Status: {result.Status}");
                Console.WriteLine($"Device ID: {result.DeviceId}");
                Console.WriteLine($"Assigned Hub: {result.AssignedHub}");
                Console.WriteLine($"Certificate Chain Present: {(result.IssuedCertificateChain != null && result.IssuedCertificateChain.Length > 0)}");
                if (result.IssuedCertificateChain != null && result.IssuedCertificateChain.Length > 0)
                {
                    Console.WriteLine($"Certificate Chain Length: {result.IssuedCertificateChain.Length} certificates");
                }

                if (result.Status == ProvisioningRegistrationStatusType.Assigned &&
                    !string.IsNullOrWhiteSpace(result.DeviceId) && !string.IsNullOrWhiteSpace(result.AssignedHub))
                {
                    // If certificate chain is issued, use X.509 authentication
                    if (result.IssuedCertificateChain != null && result.IssuedCertificateChain.Length > 0)
                    {
                        // Convert base64-encoded certs to PEM format
                        var certChainPem = ConvertCertChainToPem(result.IssuedCertificateChain);
                        CertificateManager.SaveIssuedCertificatePem(dpsCfg.IssuedCertFilePath, certChainPem);
                        Console.WriteLine($"Saved issued certificate chain to: {dpsCfg.IssuedCertFilePath}");
                        
                        var x509 = CertificateManager.LoadX509WithPrivateKey(dpsCfg.IssuedCertFilePath, dpsCfg.CsrKeyFilePath);
                        var deviceClient = DeviceClient.Create(
                            result.AssignedHub,
                            new DeviceAuthenticationWithX509Certificate(result.DeviceId, x509),
                            TransportType.Mqtt);
                        Console.WriteLine($"✅ Connected to IoT Hub via X.509. Hub={result.AssignedHub}, DeviceId={result.DeviceId}\n");
                        return deviceClient;
                    }
                    else
                    {
                        // No certificate issued - use symmetric key authentication for IoT Hub
                        // The symmetric key is the derived device key from DPS provisioning
                        var derivedDeviceKey = DpsSasTokenGenerator.DeriveDeviceKey(result.DeviceId, dpsCfg.EnrollmentGroupKeyBase64);
                        
                        var deviceClient = DeviceClient.Create(
                            result.AssignedHub,
                            new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, derivedDeviceKey),
                            TransportType.Mqtt);
                        Console.WriteLine($"✅ Connected to IoT Hub via Symmetric Key. Hub={result.AssignedHub}, DeviceId={result.DeviceId}\n");
                        return deviceClient;
                    }
                }
                else
                {
                    Console.WriteLine("DPS provisioning did not assign the device. Telemetry disabled.\n");
                    iotConfig.SendTelemetry = false;
                    return null;
                }
            }
            catch (InvalidOperationException ex)
            {
                // Connection or critical authentication errors - STOP THE APP
                Console.WriteLine($"\n❌ FATAL: DPS Connection Failed");
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Details: {ex.InnerException.Message}");
                }
                Console.WriteLine("\nThe device cannot be provisioned. Check your configuration and try again.");
                throw;  // Re-throw to stop the app
            }
            catch (Exception ex)
            {
                // Other non-critical errors - continue without telemetry
                Console.WriteLine($"Warning: DPS/IoT Hub setup failed: {ex.Message}");
                Console.WriteLine("Continuing without telemetry.\n");
                iotConfig.SendTelemetry = false;
                return null;
            }
        }

        private static string ConvertCertChainToPem(string[] base64Certs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < base64Certs.Length; i++)
            {
                // Wrap each certificate in PEM format
                sb.AppendLine("-----BEGIN CERTIFICATE-----");
                
                // Format as 64-character lines
                var cert = base64Certs[i];
                for (int j = 0; j < cert.Length; j += 64)
                {
                    sb.AppendLine(cert.Substring(j, Math.Min(64, cert.Length - j)));
                }
                
                sb.AppendLine("-----END CERTIFICATE-----");
                
                if (i < base64Certs.Length - 1)
                {
                    sb.AppendLine();  // Empty line between certificates
                }
            }
            return sb.ToString();
        }
    }
}
