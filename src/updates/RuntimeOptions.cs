using System.Text.Json;

namespace skittle_sorter;

public sealed record RuntimeOptions(string ConfigurationPath, string? ReadyFile, bool SelfTest, bool ShowVersion, bool CloudSelfTest = false, bool Calibrate = false)
{
    public static RuntimeOptions Parse(string[] args)
    {
        var config = "appsettings.json";
        string? readyFile = null;
        var selfTest = false;
        var showVersion = false;
        var cloudSelfTest = false;
        var calibrate = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    if (++i == args.Length) throw new ArgumentException("--config requires a path.");
                    config = args[i];
                    break;
                case "--ready-file":
                    if (++i == args.Length) throw new ArgumentException("--ready-file requires a path.");
                    readyFile = args[i];
                    break;
                case "--self-test":
                    selfTest = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                case "--cloud-self-test":
                    cloudSelfTest = true;
                    break;
                case "--calibrate":
                    calibrate = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }
        if (cloudSelfTest && (selfTest || showVersion || readyFile != null))
            throw new ArgumentException("--cloud-self-test cannot be combined with --self-test, --version or --ready-file.");
        if (calibrate && (cloudSelfTest || selfTest || showVersion || readyFile != null))
            throw new ArgumentException("--calibrate cannot be combined with other operating modes or --ready-file.");
        return new RuntimeOptions(Path.GetFullPath(config),
            readyFile is null ? null : Path.GetFullPath(readyFile), selfTest, showVersion, cloudSelfTest, calibrate);
    }

    public void ValidateConfiguration()
    {
        using var configuration = JsonDocument.Parse(File.ReadAllText(ConfigurationPath));
        if (configuration.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Configuration must be a JSON object.");
        if (!configuration.RootElement.TryGetProperty("Hardware", out var hardware) ||
            hardware.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("An explicit Hardware configuration is required for a service deployment.");
        if (!Calibrate)
            ColorCalibration.Load(ConfigurationPath);
    }
}
