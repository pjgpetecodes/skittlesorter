using System;
using System.Threading.Tasks;

namespace skittle_sorter
{
    public class SkittleSorterService
    {
        private readonly TCS3472x _colorSensor;
        private readonly ServoController _servo;
        private readonly TelemetryService _telemetry;
        private readonly MockColorSensorConfig _mockConfig;

        public SkittleSorterService(TCS3472x colorSensor, ServoController servo, TelemetryService telemetry, MockColorSensorConfig mockConfig)
        {
            _colorSensor = colorSensor;
            _servo = servo;
            _telemetry = telemetry;
            _mockConfig = mockConfig;
        }

        public async Task RunSortingLoopAsync()
        {
            Console.WriteLine("[SkittleSorter] Starting sorting loop...");
            
            int mockColorIndex = 0;
            
            while (true)
            {
                try
                {
                    string detectedColor;

                    if (_mockConfig.EnableMockColorSensor && _mockConfig.MockColorSequence.Count > 0)
                    {
                        detectedColor = _mockConfig.MockColorSequence[mockColorIndex % _mockConfig.MockColorSequence.Count];
                        mockColorIndex++;
                        Console.WriteLine($"[SkittleSorter] Mock color: {detectedColor}");
                    }
                    else
                    {
                        // Real sensor detection would go here
                        detectedColor = "red"; // Default for now
                        Console.WriteLine($"[SkittleSorter] Real sensor detection not implemented");
                    }

                    _telemetry.LogColorDetected(detectedColor);

                    // Move servo to correct position
                    _servo.MoveToColor(detectedColor);

                    // Send telemetry
                    await _telemetry.SendSkittleColorTelemetryAsync(detectedColor);

                    // Wait before next skittle
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SkittleSorter] Error in sorting loop: {ex.Message}");
                    await Task.Delay(500);
                }
            }
        }

        public void HomeServo()
        {
            Console.WriteLine("[SkittleSorter] Homing servo...");
            _servo.Home();
        }

        public void Stop()
        {
            Console.WriteLine("[SkittleSorter] Stopping service...");
            HomeServo();
        }
    }
}
