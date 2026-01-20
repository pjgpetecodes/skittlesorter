using System;
using System.IO;
using System.Text.Json;

namespace AzureDpsFramework.Adr
{
    public sealed class AdrConfiguration
    {
        public bool Enabled { get; set; } = false;
        public string? SubscriptionId { get; set; }
        public string? ResourceGroupName { get; set; }
        public string? NamespaceName { get; set; }

        public static AdrConfiguration Load(string appSettingsPath = "appsettings.json")
        {
            var cfg = new AdrConfiguration();
            if (!File.Exists(appSettingsPath)) return cfg; // default disabled if no file

            string json = File.ReadAllText(appSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Adr", out var adr))
            {
                return cfg; // missing section → disabled
            }

            bool GetBool(JsonElement el, string name, bool def)
            {
                return el.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;
            }
            string? GetString(JsonElement el, string name)
            {
                return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            }

            cfg.Enabled = GetBool(adr, nameof(Enabled), cfg.Enabled);
            cfg.SubscriptionId = GetString(adr, nameof(SubscriptionId));
            cfg.ResourceGroupName = GetString(adr, nameof(ResourceGroupName));
            cfg.NamespaceName = GetString(adr, nameof(NamespaceName));
            return cfg;
        }

        public bool IsConfigured()
        {
            return Enabled
                && !string.IsNullOrWhiteSpace(SubscriptionId)
                && !string.IsNullOrWhiteSpace(ResourceGroupName)
                && !string.IsNullOrWhiteSpace(NamespaceName);
        }
    }
}
