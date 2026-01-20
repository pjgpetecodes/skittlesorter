using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AzureDpsFramework.Adr
{
    public sealed class AdrDeviceUpdateConfiguration
    {
        public bool Enabled { get; set; } = false;
        public Dictionary<string, object>? Attributes { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
        public bool? DeviceEnabled { get; set; }
        public string? OperatingSystemVersion { get; set; }

        public static AdrDeviceUpdateConfiguration Load(string appSettingsPath = "appsettings.json")
        {
            var cfg = new AdrDeviceUpdateConfiguration();
            if (!File.Exists(appSettingsPath)) return cfg;

            string json = File.ReadAllText(appSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Adr", out var adr)) return cfg;
            if (!adr.TryGetProperty("DeviceUpdate", out var du) || du.ValueKind != JsonValueKind.Object) return cfg;

            bool GetBool(JsonElement e, string name, bool def)
            {
                return e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;
            }
            string? GetString(JsonElement e, string name)
            {
                return e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            }

            cfg.Enabled = GetBool(du, nameof(Enabled), cfg.Enabled);
            cfg.OperatingSystemVersion = GetString(du, nameof(OperatingSystemVersion));
            if (du.TryGetProperty("DeviceEnabled", out var devEn))
            {
                if (devEn.ValueKind == JsonValueKind.True || devEn.ValueKind == JsonValueKind.False)
                {
                    cfg.DeviceEnabled = devEn.GetBoolean();
                }
            }

            if (du.TryGetProperty("Attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
            {
                cfg.Attributes = new Dictionary<string, object>();
                foreach (var prop in attrs.EnumerateObject())
                {
                    cfg.Attributes[prop.Name] = ConvertJsonValue(prop.Value);
                }
            }

            if (du.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                cfg.Tags = new Dictionary<string, string>();
                foreach (var prop in tags.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.GetString() != null)
                    {
                        cfg.Tags[prop.Name] = prop.Value.GetString()!;
                    }
                    else
                    {
                        cfg.Tags[prop.Name] = prop.Value.ToString();
                    }
                }
            }

            return cfg;
        }

        private static object ConvertJsonValue(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out var l)) return l;
                    if (value.TryGetDouble(out var d)) return d;
                    return value.ToString();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return value.GetBoolean();
                case JsonValueKind.Null:
                    return null!;
                default:
                    return value.ToString();
            }
        }
    }
}
