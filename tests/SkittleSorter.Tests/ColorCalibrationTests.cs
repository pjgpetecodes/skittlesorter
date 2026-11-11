using System.Text.Json;
using skittle_sorter;
using Xunit;

namespace SkittleSorter.Tests;

public sealed class ColorCalibrationTests
{
    private static ColorCalibration CreateCalibration()
    {
        using var sensor = new TCS3472x(true, ColorCalibration.Labels.ToList());
        var calibration = new ColorCalibration();
        foreach (var label in ColorCalibration.Labels)
        {
            var (clear, red, green, blue) = sensor.ReadColor();
            calibration.Samples.Add(label, new ColorReading(clear, red, green, blue));
        }
        calibration.Validate();
        return calibration;
    }

    [Fact]
    public void SensorUsesConfiguredReferencesIncludingEmptyInsteadOfLegacyThresholds()
    {
        var calibration = CreateCalibration();
        // Shift the colour references so the old thresholds cannot pass this test.
        (calibration.Samples["Red"], calibration.Samples["Green"]) =
            (calibration.Samples["Green"], calibration.Samples["Red"]);
        using var sensor = new TCS3472x(true, []);
        sensor.Calibration = calibration;
        foreach (var label in ColorCalibration.Labels)
        {
            var sample = calibration.Samples[label];
            Assert.Equal(label, sensor.ClassifySkittleColor(
                (ushort)(sample.Red * 0.99), (ushort)(sample.Green * 0.99),
                (ushort)(sample.Blue * 0.99), (ushort)(sample.Clear * 0.99)));
        }
        Assert.Equal("Unknown", sensor.ClassifySkittleColor(0, 0, 0, 0));
    }

    [Fact]
    public void SaveRoundTripsInHardwareConfigAndPreservesOtherSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"Hardware":{"ServoPositions":{"DetectAngle":60}},"IoTHub":{"DeviceId":"unchanged"}}""");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            CreateCalibration().Save(path);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(60, json.RootElement.GetProperty("Hardware").GetProperty("ServoPositions").GetProperty("DetectAngle").GetInt32());
            Assert.Equal("unchanged", json.RootElement.GetProperty("IoTHub").GetProperty("DeviceId").GetString());
            var loaded = ColorCalibration.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal(CreateCalibration().Samples, loaded.Samples);
            RuntimeOptions.Parse(["--config", path, "--self-test"]).ValidateConfiguration();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingCalibrationPreservesLegacyClassification()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"Hardware":{}}""");
            Assert.Null(ColorCalibration.Load(path));
            using var sensor = new TCS3472x(true, ["Green", "None"]);
            foreach (var expected in new[] { "Green", "None" })
            {
                var (clear, red, green, blue) = sensor.ReadColor();
                Assert.Equal(expected, sensor.ClassifySkittleColor(red, green, blue, clear));
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IncompleteSaturatedAndIndistinguishableCalibrationIsRejected()
    {
        var calibration = CreateCalibration();
        calibration.Samples.Remove("Orange");
        Assert.Throws<InvalidDataException>(calibration.Validate);
        calibration = CreateCalibration();
        calibration.Samples["Red"] = new ColorReading(65535, 100, 100, 100);
        Assert.Throws<InvalidDataException>(calibration.Validate);
        calibration = CreateCalibration();
        calibration.Samples["Orange"] = calibration.Samples["Red"];
        Assert.Throws<InvalidDataException>(calibration.Validate);
        calibration = CreateCalibration();
        calibration.Samples["Green"] = new ColorReading(double.NaN, 1, 1, 1);
        Assert.Throws<InvalidDataException>(calibration.Validate);
    }

    [Fact]
    public void InvalidCalibrationCannotReplaceAnExistingConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        const string original = """{"Hardware":{"ColorCalibration":{"Samples":{}}},"IoTHub":{"DeviceId":"unchanged"}}""";
        try
        {
            File.WriteAllText(path, original);
            Assert.Throws<InvalidDataException>(() => new ColorCalibration().Save(path));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Throws<InvalidDataException>(() => ColorCalibration.Load(path));
            Assert.Throws<InvalidDataException>(() => RuntimeOptions.Parse(["--self-test", "--config", path]).ValidateConfiguration());
            // Recalibration must still be able to repair an invalid previous calibration.
            RuntimeOptions.Parse(["--calibrate", "--config", path]).ValidateConfiguration();
            CreateCalibration().Save(path);
            Assert.NotNull(ColorCalibration.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WizardCapturesTenReadingsPerLabelAndRequiresConfirmation()
    {
        var expected = CreateCalibration();
        var readings = ColorCalibration.Labels.SelectMany(label =>
            Enumerable.Repeat(expected.Samples[label], 10)).ToArray();
        var index = 0;
        var result = await ColorCalibrationWizard.CaptureAsync(
            new StringReader(new string('\n', 6) + "yes\n"), new StringWriter(),
            () => readings[index++], _ => Task.CompletedTask, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(60, index);
        Assert.Equal(expected.Samples, result.Samples);
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("")]
    [InlineData("\n\n\n\n\n\nno\n")]
    public async Task WizardCancellationDoesNotProduceCalibration(string input)
    {
        var expected = CreateCalibration();
        var readings = ColorCalibration.Labels.SelectMany(label =>
            Enumerable.Repeat(expected.Samples[label], 10)).ToArray();
        var index = 0;
        Assert.Null(await ColorCalibrationWizard.CaptureAsync(
            new StringReader(input), new StringWriter(), () => readings[index++],
            _ => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task WizardRetriesAnInvalidReadingWithoutAcceptingIt()
    {
        var expected = CreateCalibration();
        var readings = new[] { new ColorReading(0, 0, 0, 0) }.Concat(
            ColorCalibration.Labels.SelectMany(label => Enumerable.Repeat(expected.Samples[label], 10))).ToArray();
        var index = 0;
        var output = new StringWriter();
        var result = await ColorCalibrationWizard.CaptureAsync(
            new StringReader(new string('\n', 7) + "yes\n"), output,
            () => readings[index++], _ => Task.CompletedTask, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Contains("Invalid or saturated", output.ToString());
        Assert.Equal(61, index);
    }

    [Fact]
    public void CalibrationModeCannotBeCombinedWithServiceOrSelfTests()
    {
        Assert.True(RuntimeOptions.Parse(["--calibrate", "--config", "device.json"]).Calibrate);
        foreach (var other in new[] { "--version", "--self-test", "--cloud-self-test" })
            Assert.Throws<ArgumentException>(() => RuntimeOptions.Parse(["--calibrate", other]));
        Assert.Throws<ArgumentException>(() => RuntimeOptions.Parse(["--calibrate", "--ready-file", "ready.json"]));
    }
}
