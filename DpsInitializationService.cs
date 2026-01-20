using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Linq;
using Microsoft.Azure.Devices.Client;
using AzureDpsFramework;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;
using AzureDpsFramework.Adr;

namespace skittle_sorter
{
    public class DpsInitializationService
    {
        public static DeviceClient? Initialize(IoTHubConfig iotConfig)
        {
            try
            {
                var dpsCfg = DpsConfiguration.Load();

                Console.WriteLine("\n=== DPS Configuration ===\n");
                Console.WriteLine($"IdScope: {dpsCfg.IdScope}");
                Console.WriteLine($"RegistrationId: {dpsCfg.RegistrationId}");
                Console.WriteLine($"ProvisioningHost: {dpsCfg.ProvisioningHost}");
                Console.WriteLine($"AttestationMethod: {dpsCfg.AttestationMethod}");
                Console.WriteLine($"MqttPort: {dpsCfg.MqttPort}");
                Console.WriteLine($"ApiVersion: {dpsCfg.ApiVersion}");
                Console.WriteLine($"AutoGenerateCsr: {dpsCfg.AutoGenerateCsr}");

                SecurityProvider security;

                // ==============================
                // SYMMETRIC KEY ATTESTATION
                // ==============================
                if (dpsCfg.AttestationMethod == "SymmetricKey")
                {
                    Console.WriteLine($"EnrollmentGroupKeyBase64 (first 30 chars): {dpsCfg.EnrollmentGroupKeyBase64?.Substring(0, Math.Min(30, dpsCfg.EnrollmentGroupKeyBase64.Length))}...");
                    Console.WriteLine("\n=== Starting DPS Registration (Symmetric Key) ===\n");

                    if (string.IsNullOrWhiteSpace(dpsCfg.EnrollmentGroupKeyBase64))
                    {
                        throw new InvalidOperationException("EnrollmentGroupKeyBase64 is required for symmetric key attestation.");
                    }

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
                    
                    // Create security provider with CSR and enrollment group key
                    security = SecurityProviderX509Csr.CreateFromEnrollmentGroup(
                        dpsCfg.RegistrationId,
                        csrPemText,
                        privateKeyPem,
                        dpsCfg.EnrollmentGroupKeyBase64);

                    Console.WriteLine("Using symmetric-key authentication (DPS)");
                }
                // ==============================
                // X.509 ATTESTATION
                // ==============================
                else if (dpsCfg.AttestationMethod == "X509")
                {
                    Console.WriteLine($"AttestationCertPath: {dpsCfg.AttestationCertPath}");
                    Console.WriteLine($"AttestationKeyPath: {dpsCfg.AttestationKeyPath}");
                    Console.WriteLine("\n=== Starting DPS Registration (X.509) ===\n");

                    if (string.IsNullOrWhiteSpace(dpsCfg.AttestationCertPath) || !File.Exists(dpsCfg.AttestationCertPath))
                    {
                        throw new InvalidOperationException($"AttestationCertPath is required for X.509 attestation and must exist: {dpsCfg.AttestationCertPath}");
                    }

                    if (string.IsNullOrWhiteSpace(dpsCfg.AttestationKeyPath) || !File.Exists(dpsCfg.AttestationKeyPath))
                    {
                        throw new InvalidOperationException($"AttestationKeyPath is required for X.509 attestation and must exist: {dpsCfg.AttestationKeyPath}");
                    }

                    // Load existing X.509 certificate for DPS authentication
                    var authCert = CertificateManager.LoadX509WithPrivateKey(dpsCfg.AttestationCertPath, dpsCfg.AttestationKeyPath);
                    
                    // Load optional cert chain
                    System.Security.Cryptography.X509Certificates.X509Certificate2Collection? certChain = null;
                    if (!string.IsNullOrWhiteSpace(dpsCfg.AttestationCertChainPath) && File.Exists(dpsCfg.AttestationCertChainPath))
                    {
                        certChain = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
                        using (var fs = File.OpenRead(dpsCfg.AttestationCertChainPath))
                        {
                            certChain.ImportFromPemFile(dpsCfg.AttestationCertChainPath);
                        }
                    }

                    // Auto-generate CSR + key if enabled and files missing
                    if (dpsCfg.AutoGenerateCsr && (!File.Exists(dpsCfg.CsrFilePath) || !File.Exists(dpsCfg.CsrKeyFilePath)))
                    {
                        Console.WriteLine("Generating CSR and private key for new certificate request…");
                        var (csrPem, keyPem) = CertificateManager.GenerateCsr(dpsCfg.RegistrationId);
                        CertificateManager.SaveText(dpsCfg.CsrFilePath, csrPem);
                        CertificateManager.SaveText(dpsCfg.CsrKeyFilePath, keyPem);
                    }

                    var csrPemText = File.ReadAllText(dpsCfg.CsrFilePath);
                    var privateKeyPem = File.ReadAllText(dpsCfg.CsrKeyFilePath);

                    // Create security provider with existing X.509 cert for auth + CSR for new cert
                    security = new SecurityProviderX509CsrWithCert(
                        authCert,
                        dpsCfg.RegistrationId,
                        csrPemText,
                        privateKeyPem,
                        certChain);

                    Console.WriteLine("Using X.509 certificate authentication (DPS)");
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported AttestationMethod: {dpsCfg.AttestationMethod}. Use 'SymmetricKey' or 'X509'.");
                }

                // ==============================
                // COMMON PROVISIONING FLOW
                // ==============================

                // Create transport handler
                using var transport = new ProvisioningTransportHandlerMqtt(
                    dpsCfg.ProvisioningHost,
                    dpsCfg.MqttPort,
                    dpsCfg.EnableDebugLogging);

                // Create provisioning client
                using var provisioningClient = ProvisioningDeviceClient.Create(
                    dpsCfg.ProvisioningHost,
                    dpsCfg.IdScope,
                    security,
                    transport);

                Console.WriteLine($"Connecting to {dpsCfg.ProvisioningHost}:{dpsCfg.MqttPort}...");

                // Register the device
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
                        // Optional: list ADR devices after successful provisioning
                        TryListAdrDevices();

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
                        if (string.IsNullOrWhiteSpace(dpsCfg.EnrollmentGroupKeyBase64))
                        {
                            throw new InvalidOperationException("EnrollmentGroupKeyBase64 is required for symmetric key authentication");
                        }
                        
                        var derivedDeviceKey = DpsSasTokenGenerator.DeriveDeviceKey(result.DeviceId, dpsCfg.EnrollmentGroupKeyBase64);
                        
                        // Optional: list ADR devices after successful provisioning
                        TryListAdrDevices();

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

        private static void TryListAdrDevices()
        {
            try
            {
                var adrCfg = AdrConfiguration.Load();
                if (!adrCfg.IsConfigured())
                {
                    return; // disabled or not configured
                }

                using var client = new AdrDeviceRegistryClient();
                var devices = client.ListDevicesAsync(
                    adrCfg.SubscriptionId!,
                    adrCfg.ResourceGroupName!,
                    adrCfg.NamespaceName!
                ).GetAwaiter().GetResult();

                Console.WriteLine($"[ADR] Devices in namespace '{adrCfg.NamespaceName}': {devices.Count}");
                foreach (var d in devices.Take(5))
                {
                    Console.WriteLine($"[ADR] - {d.Name} ({d.Id})");
                }
                if (devices.Count > 5)
                {
                    Console.WriteLine($"[ADR] ...and {devices.Count - 5} more");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADR] Listing failed: {ex.Message}");
            }
        }
    }
}
