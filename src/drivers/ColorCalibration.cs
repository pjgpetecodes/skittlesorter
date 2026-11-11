using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace skittle_sorter;

public sealed record ColorReading(double Clear, double Red, double Green, double Blue)
{
    public void Validate(bool allowSaturation)
    {
        var channels = new[] { Clear, Red, Green, Blue };
        if (channels.Any(value => !double.IsFinite(value) || value < 0 || value > ushort.MaxValue) ||
            Clear <= 0 || (!allowSaturation && channels.Any(value => value >= ushort.MaxValue)))
            throw new InvalidDataException("Invalid or saturated sensor reading. Check lighting, sensor placement and gain, then retry.");
    }

    public double DistanceTo(ColorReading other)
    {
        return Math.Pow(Red / Clear - other.Red / other.Clear, 2) +
            Math.Pow(Green / Clear - other.Green / other.Clear, 2) +
            Math.Pow(Blue / Clear - other.Blue / other.Clear, 2) +
            Math.Pow((Clear - other.Clear) / ushort.MaxValue, 2);
    }
}

public sealed class ColorCalibration
{
    public static IReadOnlyList<string> Labels { get; } =
        Array.AsReadOnly(new[] { "None", "Red", "Green", "Yellow", "Purple", "Orange" });

    public Dictionary<string, ColorReading> Samples { get; set; } = new();

    public void Validate()
    {
        if (Samples == null || Samples.Count != Labels.Count ||
            Labels.Any(label => !Samples.ContainsKey(label)))
            throw new InvalidDataException("ColorCalibration must contain None and all five colours.");

        foreach (var label in Labels)
        {
            var sample = Samples[label] ??
                throw new InvalidDataException($"Missing calibration reading for {label}.");
            sample.Validate(allowSaturation: label == "None");
        }

        for (var i = 0; i < Labels.Count; i++)
            for (var j = i + 1; j < Labels.Count; j++)
                if (Samples[Labels[i]].DistanceTo(Samples[Labels[j]]) < 0.0001)
                    throw new InvalidDataException($"Calibration readings for {Labels[i]} and {Labels[j]} are too similar. Reposition the samples and recalibrate.");
    }

    public string Classify(ColorReading reading)
    {
        if (reading.Clear <= 0)
            return "Unknown";
        return Labels.MinBy(label => Samples[label].DistanceTo(reading))!;
    }

    public static ColorCalibration? Load(string configurationPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
        if (!document.RootElement.TryGetProperty("Hardware", out var hardware) ||
            !hardware.TryGetProperty("ColorCalibration", out var calibration))
            return null;
        var result = calibration.Deserialize<ColorCalibration>() ??
            throw new InvalidDataException("ColorCalibration cannot be null.");
        result.Validate();
        return result;
    }

    public void Save(string configurationPath)
    {
        Validate();
        var root = JsonNode.Parse(File.ReadAllText(configurationPath)) as JsonObject ??
            throw new InvalidDataException("Configuration must be a JSON object.");
        var hardware = root["Hardware"] as JsonObject ??
            throw new InvalidDataException("An explicit Hardware configuration is required for calibration.");
        hardware["ColorCalibration"] = JsonSerializer.SerializeToNode(this);
        var temporaryPath = configurationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporaryPath, fileOptions))
            using (var writer = new StreamWriter(stream))
                writer.WriteLine(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            if (OperatingSystem.IsLinux())
            {
                // Calibration may run under sudo; retain the service account's access.
                var start = new ProcessStartInfo("chown") { UseShellExecute = false };
                start.ArgumentList.Add("--reference=" + Path.GetFullPath(configurationPath));
                start.ArgumentList.Add("--");
                start.ArgumentList.Add(Path.GetFullPath(temporaryPath));
                using var process = Process.Start(start) ??
                    throw new IOException("Unable to preserve configuration ownership.");
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new IOException("Unable to preserve configuration ownership; original configuration unchanged.");
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(configurationPath));
            File.Move(temporaryPath, configurationPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
