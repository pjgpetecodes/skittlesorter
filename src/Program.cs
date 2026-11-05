using System;
using System.Device.Pwm;
using System.Device.Pwm.Drivers;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Azure.Devices.Client;
using Iot.Device.ServoMotor;
using System.Runtime.InteropServices;

namespace skittle_sorter
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var options = RuntimeOptions.Parse(args);
            if (options.ShowVersion)
            {
                Console.WriteLine(ApplicationRelease.Version);
                return;
            }
            if (options.SelfTest)
            {
                options.ValidateConfiguration();
                Console.WriteLine($"Skittle Sorter {ApplicationRelease.Version}: configuration self-test passed (no hardware or network accessed).");
                return;
            }

            if (options.ReadyFile != null)
            {
                options.ValidateConfiguration();
                File.Delete(options.ReadyFile);
            }

            using var shutdown = new CancellationTokenSource();
            ConsoleCancelEventHandler onCancel = (_, e) =>
            {
                e.Cancel = true;
                shutdown.Cancel();
            };
            Console.CancelKeyPress += onCancel;
            using var terminate = OperatingSystem.IsLinux()
                ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    shutdown.Cancel();
                })
                : null;

            Console.WriteLine($"Skittle sorter {ApplicationRelease.Version} starting…\n");

            PrintSection("Configuration");
            var mockConfig = ConfigurationLoader.LoadMockConfiguration(options.ConfigurationPath);
            var iotConfig = ConfigurationLoader.LoadIoTHubConfiguration(options.ConfigurationPath);
            var chutePositions = ConfigurationLoader.LoadChutePositions(options.ConfigurationPath);
            var servoPositions = ConfigurationLoader.LoadServoPositions(options.ConfigurationPath);
            var servoHardwareConfig = ConfigurationLoader.LoadServoSystemHardwareConfiguration(options.ConfigurationPath);

            Console.WriteLine($"Mock Mode - Color Sensor: {mockConfig.EnableMockColorSensor}, Servos: {mockConfig.EnableMockServos}");
            Console.WriteLine($"IoT Hub Telemetry Enabled: {iotConfig.SendTelemetry}\n");

            PrintSection("Hardware Initialization");
            using var colorSensor = mockConfig.EnableMockColorSensor
                ? new TCS3472x(true, mockConfig.MockColorSequence)
                : new TCS3472x();
            Console.WriteLine("Color sensor initialised.");

            using var servo = new ServoController(mockConfig.EnableMockServos, chutePositions, servoHardwareConfig);
            servo.Home();

            PrintSection("IoT Hub / DPS Initialization");
            DeviceClient? deviceClient = null;
            TelemetryService? telemetryService = null;

            try
            {
                if (iotConfig.SendTelemetry && !shutdown.IsCancellationRequested)
                {
                    deviceClient = DpsInitializationService.Initialize(iotConfig, options.ConfigurationPath, shutdown.Token);
                    if (deviceClient == null && !shutdown.IsCancellationRequested && options.ReadyFile != null)
                        throw new InvalidOperationException("The selected service profile requires telemetry, but provisioning failed.");
                    if (deviceClient != null)
                    {
                        await deviceClient.OpenAsync(shutdown.Token);
                        telemetryService = new TelemetryService(deviceClient, iotConfig.DeviceId, iotConfig);
                    }
                }

                if (options.ReadyFile != null && !shutdown.IsCancellationRequested)
                    ApplicationRelease.WriteReadiness(options.ReadyFile);

                PrintSection("Sorting Loop");
                int currentChuteAngle = chutePositions.TryGetValue("red", out int defaultAngle) ? defaultAngle : 22;
                var sessionCounts = ApplicationRelease.SessionCountsEnabled ? new SessionCounts() : null;

                // A service stop prevents the next cycle, but never interrupts an arm holding a Skittle.
                while (!shutdown.IsCancellationRequested)
                {
                    // Step 1: Pick skittle
                    servo.MoveArmToAngle(servoPositions.PickAngle);
                    Thread.Sleep(1000);

                    // Step 2: Move under sensor
                    servo.MoveArmToAngle(servoPositions.DetectAngle);
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
                        servo.MoveArmToAngle(servoPositions.PickAngle);
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
                        if (sessionCounts == null)
                            await telemetryService.SendSkittleColorTelemetryAsync(colour);
                    }

                    // Step 7: Move servo2 only if needed
                    if (targetAngle != currentChuteAngle)
                    {
                        servo.MoveChuteToAngle(targetAngle);
                        Thread.Sleep(200);
                        currentChuteAngle = targetAngle;
                    }
                    else
                    {
                        Console.WriteLine("Already at correct chute — skipping wait.");
                    }

                    // Step 8: Drop skittle
                    servo.MoveArmToAngle(servoPositions.DropAngle);
                    Thread.Sleep(1500);

                    if (sessionCounts != null)
                    {
                        sessionCounts.RecordCompletedDrop(colour);
                        Console.WriteLine($"Session count: {colour}={sessionCounts.PerColour[colour.ToLowerInvariant()]}, total={sessionCounts.Total}");
                        if (telemetryService != null)
                            await telemetryService.SendSkittleColorTelemetryAsync(colour, sessionCounts);
                    }

                    Console.WriteLine("Sorted.\n");
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                Console.WriteLine("Startup cancelled during service stop.");
            }
            finally
            {
                servo.Stop();
                deviceClient?.Dispose();
                Console.CancelKeyPress -= onCancel;
                if (options.ReadyFile != null)
                    File.Delete(options.ReadyFile);
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