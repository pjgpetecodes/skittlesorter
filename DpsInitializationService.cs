using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Azure.Devices.Client;
using AzureDpsFramework;

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
                
                var dpsClient = new DpsProvisioningClient(dpsCfg);
                Console.WriteLine("Using symmetric-key authentication (DPS)");
                Console.WriteLine($"Connecting to {dpsCfg.ProvisioningHost}:{dpsCfg.MqttPort}...");

                var resp = dpsClient.RegisterWithCsrAsync(csrPemText, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"\nDPS Response Status: {resp.status}");
                if (resp.registrationState != null)
                {
                    Console.WriteLine($"Device ID: {resp.registrationState.deviceId}");
                    Console.WriteLine($"Assigned Hub: {resp.registrationState.assignedHub}");
                    Console.WriteLine($"Certificate Chain Present: {(resp.registrationState.issuedCertificateChain != null && resp.registrationState.issuedCertificateChain.Length > 0)}");
                    if (resp.registrationState.issuedCertificateChain != null && resp.registrationState.issuedCertificateChain.Length > 0)
                    {
                        Console.WriteLine($"Certificate Chain Length: {resp.registrationState.issuedCertificateChain.Length} certificates");
                    }
                }

                if (resp.status == "assigned" && resp.registrationState != null &&
                    !string.IsNullOrWhiteSpace(resp.registrationState.deviceId) && !string.IsNullOrWhiteSpace(resp.registrationState.assignedHub))
                {
                    // If certificate chain is issued, use X.509 authentication
                    if (resp.registrationState.issuedCertificateChain != null && resp.registrationState.issuedCertificateChain.Length > 0)
                    {
                        // Convert base64-encoded certs to PEM format
                        var certChainPem = ConvertCertChainToPem(resp.registrationState.issuedCertificateChain);
                        CertificateManager.SaveIssuedCertificatePem(dpsCfg.IssuedCertFilePath, certChainPem);
                        Console.WriteLine($"Saved issued certificate chain to: {dpsCfg.IssuedCertFilePath}");
                        
                        var x509 = CertificateManager.LoadX509WithPrivateKey(dpsCfg.IssuedCertFilePath, dpsCfg.CsrKeyFilePath);
                        var deviceClient = DeviceClient.Create(
                            resp.registrationState.assignedHub,
                            new DeviceAuthenticationWithX509Certificate(resp.registrationState.deviceId, x509),
                            TransportType.Mqtt);
                        Console.WriteLine($"✅ Connected to IoT Hub via X.509. Hub={resp.registrationState.assignedHub}, DeviceId={resp.registrationState.deviceId}\n");
                        return deviceClient;
                    }
                    else
                    {
                        // No certificate issued - use symmetric key authentication for IoT Hub
                        // The symmetric key is the derived device key from DPS provisioning
                        var derivedDeviceKey = DpsSasTokenGenerator.DeriveDeviceKey(resp.registrationState.deviceId, dpsCfg.EnrollmentGroupKeyBase64);
                        
                        var deviceClient = DeviceClient.Create(
                            resp.registrationState.assignedHub,
                            new DeviceAuthenticationWithRegistrySymmetricKey(resp.registrationState.deviceId, derivedDeviceKey),
                            TransportType.Mqtt);
                        Console.WriteLine($"✅ Connected to IoT Hub via Symmetric Key. Hub={resp.registrationState.assignedHub}, DeviceId={resp.registrationState.deviceId}\n");
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
