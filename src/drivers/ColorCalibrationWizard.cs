namespace skittle_sorter;

public static class ColorCalibrationWizard
{
    public static async Task<ColorCalibration?> CaptureAsync(
        TextReader input, TextWriter output, Func<ColorReading> read,
        Func<CancellationToken, Task> waitForReading, CancellationToken cancellationToken)
    {
        var calibration = new ColorCalibration();
        foreach (var label in ColorCalibration.Labels)
        {
            while (true)
            {
                output.WriteLine(label == "None"
                    ? "Remove all Skittles; leave the empty carrier in its normal detection position."
                    : $"Place one {label} Skittle in the normal detection position.");
                output.WriteLine("Press Enter to capture 10 readings, or type q to cancel.");
                var answer = await input.ReadLineAsync(cancellationToken);
                if (answer == null || answer.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
                    return null;

                var readings = new List<ColorReading>();
                try
                {
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

                var average = new ColorReading(readings.Average(x => x.Clear), readings.Average(x => x.Red),
                    readings.Average(x => x.Green), readings.Average(x => x.Blue));
                calibration.Samples.Add(label, average);
                output.WriteLine($"{label}: C={average.Clear:F0} R={average.Red:F0} G={average.Green:F0} B={average.Blue:F0}");
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

        Console.WriteLine("Stop the sorter service before calibrating. No servos will move and no cloud connection will be made.");
        Console.WriteLine("Keep the normal lighting and sensor distance. Manually position the carrier under the sensor.");
        Console.WriteLine($"Configuration to update: {configurationPath}");
        using var sensor = new TCS3472x();
        var calibration = await CaptureAsync(Console.In, Console.Out, () =>
        {
            var (clear, red, green, blue) = sensor.ReadColor();
            return new ColorReading(clear, red, green, blue);
        }, token => Task.Delay(200, token), cancellationToken);
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
