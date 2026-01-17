using System;
using System.IO;
using System.Text.Json;

namespace AzureDpsFramework
{
    public class DpsConfiguration
    {
        public string ProvisioningHost { get; set; } = "global.azure-devices-provisioning.net";
        public string IdScope { get; set; } = string.Empty;
        public string RegistrationId { get; set; } = string.Empty;
        public string? DeviceKeyBase64 { get; set; }
        public string? EnrollmentGroupKeyBase64 { get; set; }
        public int SasExpirySeconds { get; set; } = 3600;
        public string ApiVersion { get; set; } = "2025-07-01-preview";
        public int MqttPort { get; set; } = 8883;
        public bool AutoGenerateCsr { get; set; } = true;
        public string CsrFilePath { get; set; } = "certs/device.csr";
        public string CsrKeyFilePath { get; set; } = "certs/device.key";
        public string IssuedCertFilePath { get; set; } = "certs/issued.pem";

        public static DpsConfiguration Load(string appSettingsPath = "appsettings.json")
        {
            var cfg = new DpsConfiguration();
            if (!File.Exists(appSettingsPath))
            {
                throw new InvalidOperationException("appsettings.json not found.");
            }

            string json = File.ReadAllText(appSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("IoTHub", out var iotHub))
            {
                throw new InvalidOperationException("IoTHub section missing in appsettings.json");
            }
            if (!iotHub.TryGetProperty("DpsProvisioning", out var dps))
            {
                throw new InvalidOperationException("IoTHub.DpsProvisioning section missing in appsettings.json");
            }

            string GetString(JsonElement el, string name, string def = "")
            {
                return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;
            }
            int GetInt(JsonElement el, string name, int def)
            {
                return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;
            }
            bool GetBool(JsonElement el, string name, bool def)
            {
                return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False ? v.GetBoolean() : def;
            }

            cfg.ProvisioningHost = GetString(dps, nameof(ProvisioningHost), cfg.ProvisioningHost);
            cfg.IdScope = GetString(dps, nameof(IdScope), cfg.IdScope);
            cfg.RegistrationId = GetString(dps, nameof(RegistrationId), cfg.RegistrationId);
            var deviceKey = GetString(dps, nameof(DeviceKeyBase64));
            cfg.DeviceKeyBase64 = string.IsNullOrWhiteSpace(deviceKey) ? null : deviceKey;
            var groupKey = GetString(dps, nameof(EnrollmentGroupKeyBase64));
            cfg.EnrollmentGroupKeyBase64 = string.IsNullOrWhiteSpace(groupKey) ? null : groupKey;
            cfg.SasExpirySeconds = GetInt(dps, nameof(SasExpirySeconds), cfg.SasExpirySeconds);
            cfg.ApiVersion = GetString(dps, nameof(ApiVersion), cfg.ApiVersion);
            cfg.MqttPort = GetInt(dps, nameof(MqttPort), cfg.MqttPort);
            cfg.AutoGenerateCsr = GetBool(dps, nameof(AutoGenerateCsr), cfg.AutoGenerateCsr);
            cfg.CsrFilePath = GetString(dps, nameof(CsrFilePath), cfg.CsrFilePath);
            cfg.CsrKeyFilePath = GetString(dps, nameof(CsrKeyFilePath), cfg.CsrKeyFilePath);
            cfg.IssuedCertFilePath = GetString(dps, nameof(IssuedCertFilePath), cfg.IssuedCertFilePath);

            cfg.Validate();
            return cfg;
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(IdScope)) throw new InvalidOperationException("DPS IdScope is required");
            if (string.IsNullOrWhiteSpace(RegistrationId)) throw new InvalidOperationException("DPS RegistrationId is required");
            if (string.IsNullOrWhiteSpace(ProvisioningHost)) throw new InvalidOperationException("DPS ProvisioningHost is required");
            if (string.IsNullOrWhiteSpace(DeviceKeyBase64) && string.IsNullOrWhiteSpace(EnrollmentGroupKeyBase64))
                throw new InvalidOperationException("Either DeviceKeyBase64 or EnrollmentGroupKeyBase64 must be provided");
            if (string.IsNullOrWhiteSpace(IssuedCertFilePath)) throw new InvalidOperationException("IssuedCertFilePath is required");
            if (AutoGenerateCsr)
            {
                if (string.IsNullOrWhiteSpace(CsrFilePath) || string.IsNullOrWhiteSpace(CsrKeyFilePath))
                    throw new InvalidOperationException("CsrFilePath and CsrKeyFilePath are required when AutoGenerateCsr is true");
            }
        }

        public string GetDeviceKeyOrDerive()
        {
            if (!string.IsNullOrWhiteSpace(DeviceKeyBase64)) return DeviceKeyBase64!;
            if (string.IsNullOrWhiteSpace(EnrollmentGroupKeyBase64))
                throw new InvalidOperationException("EnrollmentGroupKeyBase64 is required to derive device key");
            return DpsSasTokenGenerator.DeriveDeviceKey(RegistrationId, EnrollmentGroupKeyBase64!);
        }
    }
}
