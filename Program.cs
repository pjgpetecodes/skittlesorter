            using System;
using System.Device.Pwm;
using System.Device.Pwm.Drivers;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Azure.Devices.Client;
using Iot.Device.ServoMotor;

namespace skittle_sorter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Skittle sorter starting…\n");

            PrintSection("Configuration");
            var mockConfig = ConfigurationLoader.LoadMockConfiguration();
            var iotConfig = ConfigurationLoader.LoadIoTHubConfiguration();
            var chutePositions = ConfigurationLoader.LoadChutePositions();
            var servoPositions = ConfigurationLoader.LoadServoPositions();

            Console.WriteLine($"Mock Mode - Color Sensor: {mockConfig.EnableMockColorSensor}, Servos: {mockConfig.EnableMockServos}");
            Console.WriteLine($"IoT Hub Telemetry Enabled: {iotConfig.SendTelemetry}\n");

            PrintSection("Hardware Initialization");
            using var colorSensor = mockConfig.EnableMockColorSensor
                ? new TCS3472x(true, mockConfig.MockColorSequence)
                : new TCS3472x();
            Console.WriteLine("Color sensor initialised.");

            var servo = new ServoController(mockConfig.EnableMockServos, chutePositions);
            servo.Home();

            PrintSection("IoT Hub / DPS Initialization");
            DeviceClient? deviceClient = null;
            TelemetryService? telemetryService = null;

            if (iotConfig.SendTelemetry)
            {
                deviceClient = DpsInitializationService.Initialize(iotConfig);
                if (deviceClient != null)
                {
                    telemetryService = new TelemetryService(deviceClient, iotConfig.DeviceId, iotConfig);
                }
            }

            PrintSection("Sorting Loop");
            int currentChuteAngle = chutePositions.TryGetValue("red", out int defaultAngle) ? defaultAngle : 22;

            try
            {
                while (true)
                {
                    // Step 1: Pick skittle
                    servo.MoveToAngle(servoPositions.PickAngle);
                    Thread.Sleep(1000);

                    // Step 2: Move under sensor
                    servo.MoveToAngle(servoPositions.DetectAngle);
                    Thread.Sleep(500);

                    // Step 3: Read sensor
                    var (clear, red, green, blue) = colorSensor.ReadColor();
                    Console.WriteLine($"C={clear} R={red} G={green} B={blue}");

                    string colour = colorSensor.ClassifySkittleColor(red, green, blue, clear);
                    Console.WriteLine($"Detected: {colour}");

                    // Step 4: Handle "None" case
                    if (colour == "None")
                    {
                        Console.WriteLine("No Skittle detected — returning to pick position.\n");
                        servo.MoveToAngle(servoPositions.PickAngle);
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Step 5: Determine target chute angle
                    int targetAngle = servo.GetAngleForColor(colour);
                    if (targetAngle == -1)
                    {
                        // Color not in mapping, use current angle
                        targetAngle = currentChuteAngle;
                    }

                    // Step 6: Log and send telemetry
                    if (telemetryService != null)
                    {
                        telemetryService.LogColorDetected(colour);
                        await telemetryService.SendSkittleColorTelemetryAsync(colour);
                    }

                    // Step 7: Move servo2 only if needed
                    if (targetAngle != currentChuteAngle)
                    {
                        servo.MoveToAngle(targetAngle);
                        Thread.Sleep(200);
                        currentChuteAngle = targetAngle;
                    }
                    else
                    {
                        Console.WriteLine("Already at correct chute — skipping wait.");
                    }

                    // Step 8: Drop skittle
                    servo.MoveToAngle(servoPositions.DropAngle);
                    Thread.Sleep(1500);

                    Console.WriteLine("Sorted.\n");
                }
            }
            finally
            {
                servo.Stop();
                deviceClient?.Dispose();
            }
            static void PrintSection(string title)
            {
                Console.WriteLine($"\n=== {title} ===\n");
            }
        }
    }

    public class IoTHubConfig
    {
        public string DeviceConnectionString { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public bool SendTelemetry { get; set; } = false;
    }
}