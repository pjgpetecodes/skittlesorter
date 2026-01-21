using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;

namespace skittle_sorter
{
    public class TelemetryService
    {
        private readonly DeviceClient _deviceClient;
        private readonly string _deviceId;
        private readonly IoTHubConfig _config;

        public TelemetryService(DeviceClient deviceClient, string deviceId, IoTHubConfig config)
        {
            _deviceClient = deviceClient;
            _deviceId = deviceId;
            _config = config;
        }

        public async Task SendSkittleColorTelemetryAsync(string detectedColor)
        {
            if (!_config.SendTelemetry)
            {
                return;
            }

            try
            {
                var telemetryData = new Dictionary<string, object>
                {
                    { "color", detectedColor },
                    { "timestamp", DateTime.UtcNow },
                    { "deviceId", _deviceId }
                };

                string json = JsonSerializer.Serialize(telemetryData);
                var message = new Message(System.Text.Encoding.UTF8.GetBytes(json))
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8"
                };

                Console.WriteLine($"[Telemetry] Sending color: {detectedColor}");
                await _deviceClient.SendEventAsync(message);
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

        public void LogColorDetected(string color)
        {
            Console.WriteLine($"[Color Detected] {color}");
        }
    }
}
