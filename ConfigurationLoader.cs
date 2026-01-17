using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace skittle_sorter
{
    public class ConfigurationLoader
    {
        public static MockColorSensorConfig LoadMockConfiguration(string appSettingsPath = "appsettings.json")
        {
            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    Console.WriteLine($"Warning: {appSettingsPath} not found. Using default settings.");
                    return new MockColorSensorConfig();
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("MockMode", out var mockModeElement))
                {
                    Console.WriteLine("Warning: MockMode section not found in config. Using defaults.");
                    return new MockColorSensorConfig();
                }

                var config = new MockColorSensorConfig();

                if (mockModeElement.TryGetProperty("EnableMockColorSensor", out var enableColorSensor))
                {
                    config.EnableMockColorSensor = enableColorSensor.GetBoolean();
                }

                if (mockModeElement.TryGetProperty("EnableMockServos", out var enableServos))
                {
                    config.EnableMockServos = enableServos.GetBoolean();
                }

                if (mockModeElement.TryGetProperty("MockColorSequence", out var colorSequence))
                {
                    config.MockColorSequence = new List<string>();
                    foreach (var color in colorSequence.EnumerateArray())
                    {
                        if (color.ValueKind == JsonValueKind.String)
                        {
                            var colorValue = color.GetString();
                            if (colorValue != null)
                            {
                                config.MockColorSequence.Add(colorValue);
                            }
                        }
                    }
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading mock configuration: {ex.Message}");
                return new MockColorSensorConfig();
            }
        }

        public static IoTHubConfig LoadIoTHubConfiguration(string appSettingsPath = "appsettings.json")
        {
            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    return new IoTHubConfig();
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("IoTHub", out var iotHubElement))
                {
                    return new IoTHubConfig();
                }

                var config = new IoTHubConfig();

                if (iotHubElement.TryGetProperty("DeviceConnectionString", out var connString))
                {
                    var value = connString.GetString();
                    if (value != null && !value.StartsWith("<"))
                    {
                        config.DeviceConnectionString = value;
                    }
                }

                if (iotHubElement.TryGetProperty("DeviceId", out var deviceId))
                {
                    var value = deviceId.GetString();
                    if (value != null && !value.StartsWith("<"))
                    {
                        config.DeviceId = value;
                    }
                }

                if (iotHubElement.TryGetProperty("SendTelemetry", out var sendTelemetry))
                {
                    config.SendTelemetry = sendTelemetry.GetBoolean();
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading IoT Hub configuration: {ex.Message}");
                return new IoTHubConfig();
            }
        }

        public static Dictionary<string, int> LoadChutePositions(string appSettingsPath = "appsettings.json")
        {
            var positions = new Dictionary<string, int>
            {
                { "Red", 22 },
                { "Green", 44 },
                { "Purple", 66 },
                { "Yellow", 88 },
                { "Orange", 112 }
            };

            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    return positions;
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("ChutePositions", out var chuteElement))
                {
                    return positions;
                }

                var loaded = new Dictionary<string, int>();
                foreach (var prop in chuteElement.EnumerateObject())
                {
                    if (prop.Name != "Default" && prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        loaded[prop.Name] = prop.Value.GetInt32();
                    }
                }

                // Merge loaded positions with defaults
                foreach (var kvp in loaded)
                {
                    positions[kvp.Key] = kvp.Value;
                }

                return positions;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading chute positions: {ex.Message}");
                return positions;
            }
        }

        public static ServoPositionsConfig LoadServoPositions(string appSettingsPath = "appsettings.json")
        {
            var config = new ServoPositionsConfig();

            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    return config;
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("ServoPositions", out var servoElement))
                {
                    return config;
                }

                if (servoElement.TryGetProperty("PickAngle", out var pick))
                {
                    config.PickAngle = pick.GetInt32();
                }

                if (servoElement.TryGetProperty("DetectAngle", out var detect))
                {
                    config.DetectAngle = detect.GetInt32();
                }

                if (servoElement.TryGetProperty("DropAngle", out var drop))
                {
                    config.DropAngle = drop.GetInt32();
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading servo positions: {ex.Message}");
                return config;
            }
        }
    }

    public class ServoPositionsConfig
    {
        public int PickAngle { get; set; } = 160;
        public int DetectAngle { get; set; } = 60;
        public int DropAngle { get; set; } = 0;    }
}