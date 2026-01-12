using System;
using System.Device.Pwm;
using System.Device.Pwm.Drivers;
using System.Text.Json;
using System.Threading;
using Iot.Device.ServoMotor;

namespace robot_firmware
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Skittle sorter starting…");

            // ==============================
            // LOAD CONFIGURATION FROM FILE
            // ==============================
            var config = LoadConfiguration();

            Console.WriteLine($"Mock Mode - Color Sensor: {config.EnableMockColorSensor}, Servos: {config.EnableMockServos}\n");

            // ==============================
            // COLOR SENSOR SETUP
            // ==============================
            using var colorSensor = config.EnableMockColorSensor
                ? new TCS3472x(true, config.MockColorSequence)
                : new TCS3472x();
            Console.WriteLine("Sensor initialised.");

            // ==============================
            // SERVO SETUP
            // ==============================
            PwmChannel? pwm1 = null;
            PwmChannel? pwm2 = null;
            dynamic servo1;
            dynamic servo2;

            if (config.EnableMockServos)
            {
                servo1 = new MockServoMotor("Servo 1 (Pick)");
                servo2 = new MockServoMotor("Servo 2 (Chute)");
            }
            else
            {
                pwm1 = PwmChannel.Create(0, 0, 50);
                servo1 = new ServoMotor(pwm1, 180, 700, 2400);

                pwm2 = PwmChannel.Create(0, 1, 50);
                servo2 = new ServoMotor(pwm2, 180, 700, 2400);
            }

            servo1.Start();
            servo2.Start();

            // Track current chute position
            int currentChuteAngle = 22; // start at red

            try
            {
                while (true)
                {
                    // ------------------------------
                    // 1. PICK SKITTLE
                    // ------------------------------
                    MoveToAngle(servo1, 160);
                    Thread.Sleep(1000);

                    // ------------------------------
                    // 2. MOVE UNDER SENSOR
                    // ------------------------------
                    MoveToAngle(servo1, 60);
                    Thread.Sleep(500);

                    // ------------------------------
                    // 3. READ SENSOR
                    // ------------------------------
                    var (clear, red, green, blue) = colorSensor.ReadColor();

                    Console.WriteLine($"C={clear} R={red} G={green} B={blue}");

                    string colour = colorSensor.ClassifySkittleColor(red, green, blue, clear);
                    Console.WriteLine($"Detected: {colour}");

                    // ------------------------------
                    // 4. HANDLE "NONE" CASE
                    // ------------------------------
                    if (colour == "None")
                    {
                        Console.WriteLine("No Skittle detected — returning to pick position.");
                        MoveToAngle(servo1, 160);
                        Thread.Sleep(1000);
                        continue;
                    }

                    // ------------------------------
                    // 5. DETERMINE TARGET CHUTE ANGLE
                    // ------------------------------
                    int targetAngle = colour switch
                    {
                        "Red" => 22,
                        "Green" => 44,
                        "Purple" => 66,
                        "Yellow" => 88,
                        "Orange" => 112,
                        _ => currentChuteAngle
                    };

                    // ------------------------------
                    // 6. MOVE SERVO2 ONLY IF NEEDED
                    // ------------------------------
                    if (targetAngle != currentChuteAngle)
                    {
                        MoveToAngle(servo2, targetAngle);
                        Thread.Sleep(200);
                        currentChuteAngle = targetAngle;
                    }
                    else
                    {
                        Console.WriteLine("Already at correct chute — skipping wait.");
                    }

                    // ------------------------------
                    // 7. DROP SKITTLE
                    // ------------------------------
                    MoveToAngle(servo1, 0);
                    Thread.Sleep(1500);

                    Console.WriteLine("Sorted.\n");
                }
            }
            finally
            {
                servo1.Stop();
                servo2.Stop();
                pwm1?.Dispose();
                pwm2?.Dispose();
            }
        }

        static void MoveToAngle(dynamic servo, int angle)
        {
            servo.WriteAngle(angle);
        }

        static MockColorSensorConfig LoadConfiguration()
        {
            try
            {
                string configPath = "appsettings.json";
                
                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"Warning: {configPath} not found. Using default settings.");
                    return new MockColorSensorConfig();
                }

                string json = File.ReadAllText(configPath);
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
                    config.MockColorSequence = new();
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
                Console.WriteLine($"Error loading configuration: {ex.Message}");
                return new MockColorSensorConfig();
            }
        }
    }
}