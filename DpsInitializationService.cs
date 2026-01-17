using System;
using System.IO;
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
                var dpsClient = new DpsProvisioningClient(dpsCfg);
                Console.WriteLine("Using symmetric-key authentication (DPS)");

                var resp = dpsClient.RegisterWithCsrAsync(csrPemText, CancellationToken.None).GetAwaiter().GetResult();

                if (resp.status == "assigned" && resp.registrationState != null &&
                    !string.IsNullOrWhiteSpace(resp.registrationState.deviceId) && !string.IsNullOrWhiteSpace(resp.registrationState.assignedHub))
                {
                    // Save issued certificate chain if provided
                    if (!string.IsNullOrWhiteSpace(resp.registrationState.issuedCertificateChain))
                    {
                        CertificateManager.SaveIssuedCertificatePem(dpsCfg.IssuedCertFilePath, resp.registrationState.issuedCertificateChain);
                    }

                    var x509 = CertificateManager.LoadX509WithPrivateKey(dpsCfg.IssuedCertFilePath, dpsCfg.CsrKeyFilePath);
                    var deviceClient = DeviceClient.Create(
                        resp.registrationState.assignedHub,
                        new DeviceAuthenticationWithX509Certificate(resp.registrationState.deviceId, x509),
                        TransportType.Mqtt);
                    Console.WriteLine($"Connected to IoT Hub. Hub={resp.registrationState.assignedHub}, DeviceId={resp.registrationState.deviceId}\n");
                    return deviceClient;
                }
                else
                {
                    Console.WriteLine("DPS provisioning did not assign the device. Telemetry disabled.\n");
                    iotConfig.SendTelemetry = false;
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: DPS/IoT Hub setup failed: {ex.Message}");
                Console.WriteLine("Continuing without telemetry.\n");
                iotConfig.SendTelemetry = false;
                return null;
            }
        }
    }
}
