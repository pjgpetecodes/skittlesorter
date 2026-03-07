using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace skittle_sorter
{
    public class ConfigurationLoader
    {
        private const string RegistrationIdToken = "{RegistrationId}";

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
                var configRoot = GetHardwareRoot(root);

                if (!configRoot.TryGetProperty("MockMode", out var mockModeElement))
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
                        config.DeviceId = ResolveDeviceId(value, iotHubElement);
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
                var configRoot = GetHardwareRoot(root);

                if (!configRoot.TryGetProperty("ChutePositions", out var chuteElement))
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
                var configRoot = GetHardwareRoot(root);

                if (!configRoot.TryGetProperty("ServoPositions", out var servoElement))
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

        public static ServoHardwareConfig LoadServoHardwareConfiguration(string appSettingsPath = "appsettings.json")
        {
            var config = new ServoHardwareConfig();

            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    return config;
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var configRoot = GetHardwareRoot(root);

                if (!configRoot.TryGetProperty("ServoHardware", out var servoHardwareElement))
                {
                    return config;
                }

                if (servoHardwareElement.TryGetProperty("UseSoftwarePwm", out var useSoftwarePwm))
                {
                    config.UseSoftwarePwm = useSoftwarePwm.GetBoolean();
                }

                if (servoHardwareElement.TryGetProperty("GpioPin", out var gpioPin))
                {
                    config.GpioPin = gpioPin.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("PwmChip", out var pwmChip))
                {
                    config.PwmChip = pwmChip.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("PwmChannel", out var pwmChannel))
                {
                    config.PwmChannel = pwmChannel.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("FrequencyHz", out var frequencyHz))
                {
                    config.FrequencyHz = frequencyHz.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("AngleRange", out var angleRange))
                {
                    config.AngleRange = angleRange.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("MinPulseWidthMicroseconds", out var minPulseWidth))
                {
                    config.MinPulseWidthMicroseconds = minPulseWidth.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("MaxPulseWidthMicroseconds", out var maxPulseWidth))
                {
                    config.MaxPulseWidthMicroseconds = maxPulseWidth.GetInt32();
                }

                if (servoHardwareElement.TryGetProperty("HomeAngle", out var homeAngle))
                {
                    config.HomeAngle = homeAngle.GetInt32();
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading servo hardware configuration: {ex.Message}");
                return config;
            }
        }

        public static ServoSystemHardwareConfig LoadServoSystemHardwareConfiguration(string appSettingsPath = "appsettings.json")
        {
            var config = new ServoSystemHardwareConfig();

            try
            {
                if (!File.Exists(appSettingsPath))
                {
                    return config;
                }

                string json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var configRoot = GetHardwareRoot(root);

                if (configRoot.TryGetProperty("ArmServoHardware", out var armElement))
                {
                    config.Arm = ParseServoHardwareElement(armElement, config.Arm);
                }

                if (configRoot.TryGetProperty("ChuteServoHardware", out var chuteElement))
                {
                    config.Chute = ParseServoHardwareElement(chuteElement, config.Chute);
                }

                // Legacy fallback: if only ServoHardware exists, apply to both channels.
                if (configRoot.TryGetProperty("ServoHardware", out var legacyElement)
                    && !configRoot.TryGetProperty("ArmServoHardware", out _)
                    && !configRoot.TryGetProperty("ChuteServoHardware", out _))
                {
                    var parsed = ParseServoHardwareElement(legacyElement, config.Arm);
                    config.Arm = parsed;
                    config.Chute = new ServoHardwareConfig
                    {
                        UseSoftwarePwm = parsed.UseSoftwarePwm,
                        GpioPin = parsed.GpioPin,
                        PwmChip = parsed.PwmChip,
                        PwmChannel = parsed.UseSoftwarePwm ? parsed.PwmChannel : 1,
                        FrequencyHz = parsed.FrequencyHz,
                        AngleRange = parsed.AngleRange,
                        MinPulseWidthMicroseconds = parsed.MinPulseWidthMicroseconds,
                        MaxPulseWidthMicroseconds = parsed.MaxPulseWidthMicroseconds,
                        HomeAngle = 22
                    };
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading servo system hardware configuration: {ex.Message}");
                return config;
            }
        }

        private static ServoHardwareConfig ParseServoHardwareElement(JsonElement servoHardwareElement, ServoHardwareConfig defaults)
        {
            var config = new ServoHardwareConfig
            {
                UseSoftwarePwm = defaults.UseSoftwarePwm,
                GpioPin = defaults.GpioPin,
                PwmChip = defaults.PwmChip,
                PwmChannel = defaults.PwmChannel,
                FrequencyHz = defaults.FrequencyHz,
                AngleRange = defaults.AngleRange,
                MinPulseWidthMicroseconds = defaults.MinPulseWidthMicroseconds,
                MaxPulseWidthMicroseconds = defaults.MaxPulseWidthMicroseconds,
                HomeAngle = defaults.HomeAngle
            };

            if (servoHardwareElement.TryGetProperty("UseSoftwarePwm", out var useSoftwarePwm))
            {
                config.UseSoftwarePwm = useSoftwarePwm.GetBoolean();
            }

            if (servoHardwareElement.TryGetProperty("GpioPin", out var gpioPin))
            {
                config.GpioPin = gpioPin.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("PwmChip", out var pwmChip))
            {
                config.PwmChip = pwmChip.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("PwmChannel", out var pwmChannel))
            {
                config.PwmChannel = pwmChannel.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("FrequencyHz", out var frequencyHz))
            {
                config.FrequencyHz = frequencyHz.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("AngleRange", out var angleRange))
            {
                config.AngleRange = angleRange.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("MinPulseWidthMicroseconds", out var minPulseWidth))
            {
                config.MinPulseWidthMicroseconds = minPulseWidth.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("MaxPulseWidthMicroseconds", out var maxPulseWidth))
            {
                config.MaxPulseWidthMicroseconds = maxPulseWidth.GetInt32();
            }

            if (servoHardwareElement.TryGetProperty("HomeAngle", out var homeAngle))
            {
                config.HomeAngle = homeAngle.GetInt32();
            }

            return config;
        }

        private static string ResolveDeviceId(string configuredDeviceId, JsonElement iotHubElement)
        {
            if (!configuredDeviceId.Contains(RegistrationIdToken, StringComparison.OrdinalIgnoreCase))
            {
                return configuredDeviceId;
            }

            if (iotHubElement.TryGetProperty("DpsProvisioning", out var dpsElement) &&
                dpsElement.TryGetProperty("RegistrationId", out var registrationIdElement) &&
                registrationIdElement.ValueKind == JsonValueKind.String)
            {
                var registrationId = registrationIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(registrationId))
                {
                    return configuredDeviceId.Replace(RegistrationIdToken, registrationId, StringComparison.OrdinalIgnoreCase);
                }
            }

            return configuredDeviceId;
        }

        private static JsonElement GetHardwareRoot(JsonElement root)
        {
            if (root.TryGetProperty("Hardware", out var hardwareRoot)
                && hardwareRoot.ValueKind == JsonValueKind.Object)
            {
                return hardwareRoot;
            }

            return root;
        }
    }

    public class ServoPositionsConfig
    {
        public int PickAngle { get; set; } = 160;
        public int DetectAngle { get; set; } = 60;
        public int DropAngle { get; set; } = 0;    }
}