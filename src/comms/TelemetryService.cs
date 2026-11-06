using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Iot.Device.Models.Telemetry;
using Microsoft.Azure.Iot.Device.Unified.Telemetry;

namespace skittle_sorter
{
    public class TelemetryService
    {
        private readonly TelemetryClient _deviceClient;
        private readonly string _deviceId;
        private readonly IoTHubConfig _config;

        public TelemetryService(TelemetryClient deviceClient, string deviceId, IoTHubConfig config)
        {
            _deviceClient = deviceClient;
            _deviceId = deviceId;
            _config = config;
        }

        public async Task SendSkittleColorTelemetryAsync(string detectedColor, SessionCounts? counts = null)
        {
            if (!_config.SendTelemetry)
            {
                return;
            }

            try
            {
                Console.WriteLine($"[Telemetry] Sending color: {detectedColor}");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await SendRequiredAsync(detectedColor, counts, timeout.Token);
                Console.WriteLine($"[Telemetry] Message sent successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telemetry] Error sending message: {ex.Message}");
                Console.WriteLine($"[Telemetry] Exception Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[Telemetry] Inner Exception: {ex.InnerException.Message}");
                }
            }
        }

        public Task SendRequiredAsync(string detectedColor, SessionCounts? counts = null, CancellationToken cancellationToken = default) =>
            _deviceClient.SendTelemetryAsync(new DeviceToCloudTelemetry
            {
                Payload = JsonSerializer.SerializeToUtf8Bytes(CreateTelemetry(detectedColor, _deviceId, counts)),
                ContentType = "application/json",
                ContentEncoding = "utf-8"
            }, cancellationToken);

        public void LogColorDetected(string color)
        {
            Console.WriteLine($"[Color Detected] {color}");
        }

        public static Dictionary<string, object> CreateTelemetry(string detectedColor, string deviceId, SessionCounts? counts)
        {
            var data = new Dictionary<string, object>
            {
                { "color", detectedColor },
                { "timestamp", DateTime.UtcNow },
                { "deviceId", deviceId },
                { "appVersion", ApplicationRelease.Version }
            };
            if (counts != null)
            {
                data["sessionTotal"] = counts.Total;
                data["sessionCounts"] = new Dictionary<string, long>(counts.PerColour, StringComparer.OrdinalIgnoreCase);
            }
            return data;
        }
    }
}
