using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AzureSoftwareUpdatesFramework;

public sealed class UpdateStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public UpdateStateStore(string path)
    {
        PathName = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        State = Load(PathName);
    }

    public string PathName { get; }
    public SoftwareUpdatesState State { get; private set; }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(PathName)!;
        var temporary = PathName + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + ".new";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(State, Options);
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, PathName, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static SoftwareUpdatesState Load(string path)
    {
        if (!File.Exists(path))
            return new SoftwareUpdatesState();
        try
        {
            using var stream = File.OpenRead(path);
            var state = JsonSerializer.Deserialize<SoftwareUpdatesState>(stream, Options)
                ?? throw new InvalidDataException("Updater state was empty.");
            if (state.FormatVersion != 1)
                throw new InvalidDataException($"Unsupported updater state format {state.FormatVersion}.");
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Updater state is corrupt; refusing to discard installed version or owed reports.", exception);
        }
    }
}

public sealed class ProvisioningSessionLock : IAsyncDisposable
{
    private readonly FileStream _stream;

    private ProvisioningSessionLock(FileStream stream) => _stream = stream;

    public static async Task<ProvisioningSessionLock> AcquireAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new ProvisioningSessionLock(new FileStream(
                    fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                throw new UnauthorizedAccessException(
                    $"Cannot acquire the shared sorter/DPS provisioning lock '{fullPath}'.");
            }
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Another sorter provisioning or DPS session owns '{fullPath}'.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class VerifiedPayloadDownloader : IDisposable
{
    private readonly HttpClient _httpClient;

    public VerifiedPayloadDownloader(HttpMessageHandler? handler = null)
    {
        _httpClient = new HttpClient(handler ?? new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CheckCertificateRevocationList = true
            })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<string> DownloadAsync(
        VerifiedApplicationManifest manifest,
        string directory,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (manifest.FileSize > maximumBytes)
            throw new InvalidDataException($"Payload size {manifest.FileSize} exceeds configured limit {maximumBytes}.");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, $"payload-{Guid.NewGuid():N}.tar.gz");
        var temporary = destination + ".part";
        var uri = manifest.DownloadUri;
        try
        {
            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (var redirect = 0; ; redirect++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidDataException("Payload downloads and every redirect must use HTTPS.");
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirect >= 5 || response.Headers.Location is null)
                        throw new HttpRequestException("Payload URL exceeded the HTTPS redirect limit.");
                    uri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(uri, response.Headers.Location);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length &&
                    (length != manifest.FileSize || length > maximumBytes))
                    throw new InvalidDataException("Payload Content-Length does not match the signed manifest size.");

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[64 * 1024];
                long total = 0;
                await using (var output = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                            break;
                        total += read;
                        if (total > maximumBytes || total > manifest.FileSize)
                            throw new InvalidDataException("Downloaded payload exceeds its signed size.");
                        incrementalHash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                if (total != manifest.FileSize)
                    throw new InvalidDataException("Downloaded payload is shorter than its signed size.");
                var actualHash = incrementalHash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actualHash, manifest.FileSha256))
                    throw new CryptographicException("Downloaded payload SHA-256 does not match its signed manifest hash.");
                File.Move(temporary, destination);
                return destination;
            }
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class SkittleSorterInstaller
{
    private readonly SoftwareUpdatesConfiguration _configuration;

    public SkittleSorterInstaller(SoftwareUpdatesConfiguration configuration) => _configuration = configuration;

    public async Task InstallAsync(string payloadPath, UpdateId updateId, CancellationToken cancellationToken)
    {
        var fullPayloadPath = Path.GetFullPath(payloadPath);
        var incomingDirectory = Path.GetFullPath(SoftwareUpdatesConfiguration.FixedIncomingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPayloadPath.StartsWith(incomingDirectory, StringComparison.Ordinal) ||
            Path.GetFileName(fullPayloadPath) != fullPayloadPath[incomingDirectory.Length..] ||
            !Path.GetFileName(fullPayloadPath).EndsWith(".tar.gz", StringComparison.Ordinal))
            throw new InvalidDataException("Installer helper accepts only a verified .tar.gz basename from its fixed incoming directory.");

        var startInfo = InstallerHelperCommand.Create(fullPayloadPath, updateId.Version, _configuration.Runtime);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Could not start the shared Skittle Sorter installer.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Root installer helper exited {process.ExitCode}. stdout: {Limit(stdout)} stderr: {Limit(stderr)}");
    }

    private static string Limit(string text) => text.Length <= 2048 ? text : text[..2048];
}

public static class InstallerHelperCommand
{
    public static System.Diagnostics.ProcessStartInfo Create(
        string payloadPath, string version, string runtime)
    {
        var fullPayloadPath = Path.GetFullPath(payloadPath);
        var incomingDirectory = Path.GetFullPath(SoftwareUpdatesConfiguration.FixedIncomingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var basename = Path.GetFileName(fullPayloadPath);
        if (!fullPayloadPath.StartsWith(incomingDirectory, StringComparison.Ordinal) ||
            basename != fullPayloadPath[incomingDirectory.Length..] ||
            !basename.EndsWith(".tar.gz", StringComparison.Ordinal) ||
            basename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Installer helper accepts only a verified .tar.gz basename from its fixed incoming directory.");
        if (string.IsNullOrWhiteSpace(version) || version.StartsWith('-') ||
            runtime is not ("linux-arm" or "linux-arm64"))
            throw new InvalidDataException("Installer version or runtime is invalid.");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = SoftwareUpdatesConfiguration.FixedSudoPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-n",
                     SoftwareUpdatesConfiguration.FixedInstallerHelperPath,
                     "install",
                     "--payload", basename,
                     "--version", version,
                     "--runtime", runtime
                 })
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }
}
