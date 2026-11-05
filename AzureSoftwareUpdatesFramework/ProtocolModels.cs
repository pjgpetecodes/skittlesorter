using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzureSoftwareUpdatesFramework;

public static class SoftwareUpdatesProtocol
{
    public const string ApiVersion = "2026-11-02-preview";
    public const string GetUpdateOperation = "iotdps-get-deviceupdate";
    public const string ReportStatusOperation = "iotdps-report-deviceupdatestatus";
    public const string SupportedHandler = "skittlesorter/application:1";

    public static string RequestTopic(string operation, string requestId) =>
        $"$dps/registrations/POST/{operation}/?$rid={Uri.EscapeDataString(requestId)}";

    public static string ResponseSubscription => "$dps/registrations/res/#";

    public static string GetUpdateRequest(
        AgentInformation agentInfo,
        UpdateId? installedUpdateId,
        string? agentInfoEtag,
        string? serviceConfigEtag)
    {
        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(agentInfoEtag))
        {
            payload["agentInfoEtag"] = agentInfoEtag;
        }
        else
        {
            payload["agentInfo"] = agentInfo;
        }

        if (installedUpdateId is not null)
        {
            payload["installedUpdateId"] = installedUpdateId;
        }

        if (!string.IsNullOrWhiteSpace(serviceConfigEtag))
        {
            payload["serviceConfigEtag"] = serviceConfigEtag;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string ReportRequest(InstallReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed record UpdateId(string Provider, string Name, string Version);

public sealed record AgentInformation(
    string AgentSdkVersion,
    string AgentProfile,
    IReadOnlyDictionary<string, string> CompatibilityProperties);

public sealed record UpdateCheckResponse(
    string? AgentInfoEtag,
    string? ServiceConfigEtag,
    OfferedUpdate? Update);

public sealed record OfferedUpdate(
    string WorkflowId,
    string UpdateManifest,
    string UpdateManifestSignature,
    IReadOnlyDictionary<string, string> FileUrls);

public sealed record InstallReport(
    string WorkflowId,
    UpdateId? InstalledUpdateId,
    InstallResult InstallResult);

public sealed record InstallResult(
    string Outcome,
    string FailureOrigin,
    int ResultCode,
    string ExtendedResultCodes,
    string? ResultDetails,
    IReadOnlyDictionary<string, InstallStepResult>? StepResults);

public sealed record InstallStepResult(
    string Outcome,
    string FailureOrigin,
    int ResultCode,
    string ExtendedResultCodes,
    string? ResultDetails = null);

public sealed class DpsOperationException : Exception
{
    public DpsOperationException(
        int statusCode,
        int? errorCode,
        string? serviceCode,
        string requestId,
        string? trackingId,
        TimeSpan? retryAfter,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ServiceCode = serviceCode;
        RequestId = requestId;
        TrackingId = trackingId;
        RetryAfter = retryAfter;
    }

    public int StatusCode { get; }
    public int? ErrorCode { get; }
    public string? ServiceCode { get; }
    public string RequestId { get; }
    public string? TrackingId { get; }
    public TimeSpan? RetryAfter { get; }

    public bool IsRetryable =>
        StatusCode == 429 || StatusCode is 500 or 502 or 503 or 504;
}

public sealed record DpsResponseTopicInfo(int StatusCode, string RequestId, TimeSpan? RetryAfter);

public static class DpsResponseTopicParser
{
    public static bool TryParse(string topic, out DpsResponseTopicInfo? response)
    {
        response = null;
        var topicParts = topic.Split('?', 2);
        if (topicParts.Length != 2)
            return false;
        var path = topicParts[0].Split('/');
        if (path.Length != 5 || path[0] != "$dps" || path[1] != "registrations" ||
            path[2] != "res" || !int.TryParse(path[3], out var status))
            return false;
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in topicParts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            if (!query.TryAdd(key, value))
                return false;
        }
        if (!query.TryGetValue("$rid", out var requestId) || string.IsNullOrEmpty(requestId))
            return false;
        TimeSpan? retryAfter = query.TryGetValue("retry-after", out var seconds) &&
                               int.TryParse(seconds, out var parsedSeconds) && parsedSeconds >= 0
            ? TimeSpan.FromSeconds(parsedSeconds)
            : null;
        response = new DpsResponseTopicInfo(status, requestId, retryAfter);
        return true;
    }
}

public interface IDpsSoftwareUpdatesChannel : IAsyncDisposable
{
    Task<UpdateCheckResponse> CheckForUpdateAsync(CancellationToken cancellationToken);
    Task ReportAsync(InstallReport report, CancellationToken cancellationToken);
}

public sealed class SoftwareUpdatesConfiguration
{
    public const string FixedIncomingDirectory = "/var/cache/skittlesorter-updates/incoming";
    public const string FixedSudoPath = "/usr/bin/sudo";
    public const string FixedInstallerHelperPath = "/usr/lib/skittlesorter/installer-helper";
    public const long InstallerHelperMaximumPayloadBytes = 256L * 1024 * 1024;
    public const string InstallerJournalPath = "/opt/skittlesorter/.installer-journal.json";

    public required string IdScope { get; init; }
    public required string RegistrationId { get; init; }
    public string GlobalEndpoint { get; init; } = "global.azure-devices-provisioning.net";
    public required string CertificatePath { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string StatePath { get; init; } = "/var/lib/skittlesorter/software-updates-state.json";
    public string ProvisioningLockPath { get; init; } = "/var/lib/skittlesorter/provisioning.lock";
    public string DownloadDirectory { get; init; } = FixedIncomingDirectory;
    public string Runtime { get; init; } = "linux-arm64";
    public int PollIntervalHours { get; init; } = 4;
    public long MaximumPayloadBytes { get; init; } = InstallerHelperMaximumPayloadBytes;
    public int RequestTimeoutSeconds { get; init; } = 60;
    public required AgentInformation AgentInfo { get; init; }
    public required IReadOnlyList<TrustedRootKey> TrustedRootKeys { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(IdScope) || string.IsNullOrWhiteSpace(RegistrationId))
            throw new InvalidOperationException("DPS idScope and registrationId are required.");
        if (string.IsNullOrWhiteSpace(GlobalEndpoint) ||
            GlobalEndpoint.Contains('/') || GlobalEndpoint.Contains(':'))
            throw new InvalidOperationException("globalEndpoint must be a DNS hostname.");
        if (!File.Exists(CertificatePath))
            throw new FileNotFoundException("The X.509 certificate file was not found.", CertificatePath);
        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
            throw new FileNotFoundException("The X.509 private key file was not found.", PrivateKeyPath);
        foreach (var path in new[] { StatePath, ProvisioningLockPath, DownloadDirectory })
        {
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidOperationException("Updater state, lock, download, sudo, and helper paths must be absolute.");
        }
        if (Path.GetFullPath(DownloadDirectory).TrimEnd(Path.DirectorySeparatorChar) !=
            Path.GetFullPath(FixedIncomingDirectory).TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException(
                "downloadDirectory must be the fixed helper input directory /var/cache/skittlesorter-updates/incoming.");
        if (TrustedRootKeys.Count == 0)
            throw new InvalidOperationException(
                "At least one independently trusted root key must be configured; service URLs are not trust anchors.");
        if (TrustedRootKeys.Select(key => key.Kid).Distinct(StringComparer.Ordinal).Count() != TrustedRootKeys.Count)
            throw new InvalidOperationException("Trusted root key identifiers must be unique.");
        if (PollIntervalHours < 4)
            throw new InvalidOperationException("Polling must be at least four hours; use --check-once for a bounded demo check.");
        if (MaximumPayloadBytes <= 0 ||
            MaximumPayloadBytes > InstallerHelperMaximumPayloadBytes ||
            RequestTimeoutSeconds is < 1 or > 600)
            throw new InvalidOperationException("Payload limit or request timeout is invalid.");
        if (Runtime is not ("linux-arm" or "linux-arm64"))
            throw new InvalidOperationException("runtime must be linux-arm or linux-arm64.");
        if (AgentInfo.CompatibilityProperties.Count is < 1 or > 5)
            throw new InvalidOperationException("agentInfo requires 1–5 compatibility properties.");
        if (string.IsNullOrWhiteSpace(AgentInfo.AgentProfile))
            throw new InvalidOperationException("agentInfo.agentProfile must identify the configured supported capability profile.");
    }
}

public sealed record TrustedRootKey(string Kid, string Modulus, string Exponent, bool Disabled = false);

public sealed record SoftwareUpdatesState
{
    public int FormatVersion { get; init; } = 1;
    public UpdateId? InstalledUpdateId { get; set; }
    public string? AgentInfoEtag { get; set; }
    public string? ServiceConfigEtag { get; set; }
    public ActiveWorkflow? ActiveWorkflow { get; set; }
    public InstallReport? OwedReport { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? RetryAfterUtc { get; set; }
    public string? FailedWorkflowId { get; set; }
}

public sealed record ActiveWorkflow
{
    public required string WorkflowId { get; init; }
    public required string Phase { get; set; }
    public UpdateId? PreviousInstalledUpdateId { get; init; }
    public required UpdateId TargetUpdateId { get; init; }
}
