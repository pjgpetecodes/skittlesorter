using System.Reflection;
using System.Text.Json;

namespace skittle_sorter;

public static class ApplicationRelease
{
    public static string Version =>
        typeof(ApplicationRelease).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];

#if SESSION_COUNTS
    public const bool SessionCountsEnabled = true;
#else
    public const bool SessionCountsEnabled = false;
#endif

    public static void WriteReadiness(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Environment.ProcessId + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new
            {
                version = Version,
                processId = Environment.ProcessId,
                readyAt = DateTimeOffset.UtcNow
            }));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
