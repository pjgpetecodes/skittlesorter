namespace skittle_sorter;

public enum CalibrationArmPosition { Load, Detect, Exit }

public static class ColorCalibrationWizard
{
    public static async Task<ColorCalibration?> CaptureAsync(
        TextReader input, TextWriter output, Func<ColorReading> read,
        Func<CancellationToken, Task> waitForReading,
        Func<CalibrationArmPosition, CancellationToken, Task> moveArm, CancellationToken cancellationToken)
    {
        var calibration = new ColorCalibration();
        await moveArm(CalibrationArmPosition.Load, cancellationToken);
        foreach (var label in ColorCalibration.Labels)
        {
            while (true)
            {
                output.WriteLine(label == "None"
                    ? "Leave the carrier empty at the load position."
                    : $"Insert one {label} Skittle at the load position.");
                output.WriteLine("Keep hands clear, then press Enter to move under the sensor and capture 10 readings, or type q to cancel.");
                var answer = await input.ReadLineAsync(cancellationToken);
                if (answer == null || answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    return null;

                cancellationToken.ThrowIfCancellationRequested();
                var readings = new List<ColorReading>();
                var atDetect = false;
                try
                {
                    await moveArm(CalibrationArmPosition.Detect, CancellationToken.None);
                    atDetect = true;
                    for (var i = 0; i < 10; i++)
                    {
                        await waitForReading(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        var reading = read();
                        reading.Validate(allowSaturation: label == "None");
                        readings.Add(reading);
                    }
                }
                catch (InvalidDataException ex)
                {
                    output.WriteLine(ex.Message);
                    continue;
                }
                finally
                {
                    if (atDetect)
                    {
                        // Finish unloading even if Ctrl+C arrives while holding a sample.
                        await moveArm(CalibrationArmPosition.Exit, CancellationToken.None);
                        await moveArm(CalibrationArmPosition.Load, CancellationToken.None);
                    }
                }

                var average = new ColorReading(readings.Average(x => x.Clear), readings.Average(x => x.Red),
                    readings.Average(x => x.Green), readings.Average(x => x.Blue));
                output.WriteLine($"{label}: C={average.Clear:F0} R={average.Red:F0} G={average.Green:F0} B={average.Blue:F0}");
                try
                {
                    calibration.ValidateSample(label, average);
                }
                catch (InvalidDataException ex)
                {
                    output.WriteLine(ex.Message);
                    continue;
                }
                calibration.Samples.Add(label, average);
                break;
            }
        }

        calibration.Validate();
        output.WriteLine("Save this calibration to the selected configuration? Type yes to save; anything else cancels.");
        var confirmation = await input.ReadLineAsync(cancellationToken);
        return string.Equals(confirmation?.Trim(), "yes", StringComparison.OrdinalIgnoreCase) ? calibration : null;
    }

    public static async Task RunAsync(string configurationPath, CancellationToken cancellationToken)
    {
        var mock = ConfigurationLoader.LoadMockConfiguration(configurationPath);
        if (mock.EnableMockColorSensor)
            throw new InvalidOperationException("Calibration requires a real sensor. Set Hardware.MockMode.EnableMockColorSensor to false.");

        var positions = ConfigurationLoader.LoadServoPositions(configurationPath);
        var hardware = ConfigurationLoader.LoadServoSystemHardwareConfiguration(configurationPath);
        if (new[] { positions.PickAngle, positions.DetectAngle, positions.DropAngle }
            .Any(angle => angle < 0 || angle > hardware.Arm.AngleRange))
            throw new InvalidDataException("Calibration arm positions must be within ArmServoHardware.AngleRange.");

        Console.WriteLine("Stop the sorter service before calibrating. The arm will move; keep hands clear during movement.");
        Console.WriteLine("The chute stays in place: samples exit without colour sorting. No cloud connection will be made.");
        Console.WriteLine("Keep the hopper empty except for the single requested sample; collect samples at the current chute outlet.");
        Console.WriteLine("Keep the normal lighting and sensor distance. The arm pauses at load until you press Enter.");
        Console.WriteLine($"Configuration to update: {configurationPath}");
        using var sensor = new TCS3472x();
        using var servo = new ServoController(mock.EnableMockServos, hardwareConfig: hardware);
        var calibration = await CaptureAsync(Console.In, Console.Out, () =>
        {
            var (clear, red, green, blue) = sensor.ReadColor();
            return new ColorReading(clear, red, green, blue);
        }, token => Task.Delay(200, token), async (position, token) =>
        {
            var (angle, delay) = position switch
            {
                CalibrationArmPosition.Load => (positions.PickAngle, 1000),
                CalibrationArmPosition.Detect => (positions.DetectAngle, 500),
                CalibrationArmPosition.Exit => (positions.DropAngle, 1500),
                _ => throw new ArgumentOutOfRangeException(nameof(position))
            };
            servo.MoveArmToAngle(angle);
            await Task.Delay(delay, token);
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (calibration == null)
        {
            Console.WriteLine("Calibration cancelled; files unchanged.");
            return;
        }
        calibration.Save(configurationPath);
        Console.WriteLine($"Calibration saved to {configurationPath}. Restart the sorter to use it.");
    }
}
