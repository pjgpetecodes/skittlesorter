using System.Security.Cryptography;
using System.Text.Json;
using AzureSoftwareUpdatesFramework;

return await UpdaterHost.RunAsync(args);

internal static class UpdaterHost
{
    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var checkOnce = args.Contains("--check-once", StringComparer.Ordinal);
        var configPath = ReadConfigPath(args);
        if (configPath is null)
        {
            PrintUsage();
            return 2;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            var configuration = await LoadConfigurationAsync(configPath, shutdown.Token).ConfigureAwait(false);
            configuration.Validate();
            var store = new UpdateStateStore(configuration.StatePath);
            await using var channel = new ReconnectingDpsSoftwareUpdatesChannel(async token =>
            {
                var session = new DpsMqttSoftwareUpdatesChannel(configuration, configuration.AgentInfo, store);
                try
                {
                    await session.ConnectAsync(token).ConfigureAwait(false);
                    return session;
                }
                catch
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }, async token => await ProvisioningSessionLock.AcquireAsync(
                configuration.ProvisioningLockPath, TimeSpan.FromSeconds(30), token).ConfigureAwait(false));
            using var downloader = new VerifiedPayloadDownloader();
            var installer = new SkittleSorterInstaller(configuration);

            Console.WriteLine(
                $"DPS update client configured as '{configuration.RegistrationId}' using API {SoftwareUpdatesProtocol.ApiVersion}. " +
                "No IoT Hub connection is opened.");
            do
            {
                shutdown.Token.ThrowIfCancellationRequested();
                try
                {
                    await RecoverInterruptedWorkflowAsync(store, channel, shutdown.Token).ConfigureAwait(false);
                    await SendOwedReportAsync(store, channel, shutdown.Token).ConfigureAwait(false);
                    var response = await RetryTransientAsync(
                        () => channel.CheckForUpdateAsync(shutdown.Token), shutdown.Token).ConfigureAwait(false);
                    if (response.Update is null)
                    {
                        Console.WriteLine("DPS returned HTTP 200 without updateMetadata: no update is currently offered.");
                    }
                    else
                    {
                        await HandleOfferAsync(
                            configuration, store, channel, downloader, installer, response.Update, shutdown.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (!checkOnce && exception is (IOException or TimeoutException))
                {
                    Console.Error.WriteLine(
                        $"DPS session recovery exhausted this cycle; owed reports remain stored. Next scheduled check will reopen it: {exception.Message}");
                }

                if (checkOnce)
                    break;
                await Task.Delay(TimeSpan.FromHours(configuration.PollIntervalHours), shutdown.Token)
                    .ConfigureAwait(false);
            } while (!shutdown.IsCancellationRequested);

            return 0;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Console.WriteLine("Stopping updater; any installation already started is allowed to finish.");
            return 0;
        }
        catch (DpsOperationException exception)
        {
            Console.Error.WriteLine(
                $"DPS update operation failed (HTTP {exception.StatusCode}, code {exception.ServiceCode ?? exception.ErrorCode?.ToString() ?? "unknown"}, " +
                $"request {exception.RequestId}, tracking {exception.TrackingId ?? "unavailable"}): {exception.Message}");
            return exception.IsRetryable ? 10 : 11;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Updater stopped: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task HandleOfferAsync(
        SoftwareUpdatesConfiguration configuration,
        UpdateStateStore store,
        IDpsSoftwareUpdatesChannel channel,
        VerifiedPayloadDownloader downloader,
        SkittleSorterInstaller installer,
        OfferedUpdate offer,
        CancellationToken cancellationToken)
    {
        var state = store.State;
        if (state.ActiveWorkflow is not null)
        {
            Console.Error.WriteLine(
                $"Deferred workflow '{offer.WorkflowId}'; persisted workflow '{state.ActiveWorkflow.WorkflowId}' must settle first.");
            return;
        }

        if (state.RetryAfterUtc is { } retryAt && state.FailedWorkflowId == offer.WorkflowId &&
            state.ConsecutiveFailures > 0 &&
            state.OwedReport is null && retryAt > DateTimeOffset.UtcNow)
        {
            Console.WriteLine(
                $"Workflow retry is durably deferred until {retryAt:O}; no installation will occur before then.");
            return;
        }

        if (state.FailedWorkflowId != offer.WorkflowId)
        {
            state.ConsecutiveFailures = 0;
            state.RetryAfterUtc = null;
            state.FailedWorkflowId = null;
        }

        VerifiedApplicationManifest manifest;
        try
        {
            manifest = SignedManifestVerifier.Verify(offer, configuration.TrustedRootKeys, configuration.Runtime);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidDataException or
                                           NotSupportedException or JsonException or FormatException)
        {
            var unsupported = exception is NotSupportedException;
            var failure = FailureReport(
                offer.WorkflowId,
                state.InstalledUpdateId,
                unsupported ? $"Unsupported update rejected before installation: {exception.Message}" :
                    $"Manifest verification failed: {exception.Message}",
                unsupported ? 0xF0000000 : 0x10000000,
                unsupported ? -15 : -1);
            await SetTerminalReportAsync(store, failure, failed: true, cancellationToken).ConfigureAwait(false);
            Console.Error.WriteLine($"Rejected update before download: {exception.Message}");
            return;
        }

        if (state.InstalledUpdateId == manifest.UpdateId)
        {
            var skipped = TerminalReport(offer.WorkflowId, state.InstalledUpdateId, "SKIPPED", 700, 0, "already installed");
            await SetTerminalReportAsync(store, skipped, failed: false, cancellationToken).ConfigureAwait(false);
            await SendOwedReportAsync(store, channel, cancellationToken).ConfigureAwait(false);
            return;
        }

        state.ActiveWorkflow = new ActiveWorkflow
        {
            WorkflowId = offer.WorkflowId,
            Phase = "Verified",
            PreviousInstalledUpdateId = state.InstalledUpdateId,
            TargetUpdateId = manifest.UpdateId
        };
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);

        state.OwedReport = new InstallReport(
            offer.WorkflowId,
            state.InstalledUpdateId,
            new InstallResult("IN_PROGRESS", "NOT_APPLICABLE", 1, "00000000", null, null));
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        await SendOwedReportAsync(store, channel, cancellationToken).ConfigureAwait(false);

        string? payloadPath = null;
        try
        {
            state.ActiveWorkflow.Phase = "Downloading";
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            payloadPath = await downloader.DownloadAsync(
                manifest, configuration.DownloadDirectory, configuration.MaximumPayloadBytes, cancellationToken)
                .ConfigureAwait(false);

            state.ActiveWorkflow.Phase = "Installing";
            await store.SaveAsync(cancellationToken).ConfigureAwait(false);
            // Once the installer starts, do not cancel it on service stop: its transaction and health rollback own recovery.
            await installer.InstallAsync(payloadPath, manifest.UpdateId, CancellationToken.None).ConfigureAwait(false);

            state.InstalledUpdateId = manifest.UpdateId;
            state.ActiveWorkflow = null;
            var success = TerminalReport(
                offer.WorkflowId, manifest.UpdateId, "SUCCEEDED", 700, 0, "installer completed successfully");
            await SetTerminalReportAsync(store, success, failed: false, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Installed update {manifest.UpdateId.Provider}/{manifest.UpdateId.Name}/{manifest.UpdateId.Version}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var wasDownloading = state.ActiveWorkflow?.Phase == "Downloading";
            var facility = (uint)(exception is CryptographicException ? 0x30000000 :
                wasDownloading ? 0x20000000 : 0x50000000);
            var code = exception is CryptographicException ? -3 : wasDownloading ? -2 : -5;
            state.InstalledUpdateId = state.ActiveWorkflow?.PreviousInstalledUpdateId;
            state.ActiveWorkflow = null;
            var failed = FailureReport(offer.WorkflowId, state.InstalledUpdateId, exception.Message, facility, code);
            await SetTerminalReportAsync(store, failed, failed: true, CancellationToken.None).ConfigureAwait(false);
            Console.Error.WriteLine($"Update failed; the prior installed id is retained: {exception.Message}");
        }
        finally
        {
            if (payloadPath is not null && File.Exists(payloadPath))
                File.Delete(payloadPath);
        }

        await SendOwedReportAsync(store, channel, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task RecoverInterruptedWorkflowAsync(
        UpdateStateStore store,
        IDpsSoftwareUpdatesChannel channel,
        CancellationToken cancellationToken)
    {
        if (store.State.ActiveWorkflow is not { } active)
            return;

        if (active.Phase == "Installing")
            throw new InvalidOperationException(
                $"Workflow '{active.WorkflowId}' stopped at the installer boundary. Reconcile " +
                $"'{SoftwareUpdatesConfiguration.InstallerJournalPath}' and the active release before reporting a terminal result; outcome is deliberately not guessed.");

        // The common installer owns activation rollback. An incomplete journal is never proof of success.
        store.State.InstalledUpdateId = active.PreviousInstalledUpdateId;
        store.State.ActiveWorkflow = null;
        var report = FailureReport(
            active.WorkflowId, store.State.InstalledUpdateId,
            "Updater resumed an interrupted workflow before installation began; no installer was invoked.",
            0xF0000000, -15);
        await SetTerminalReportAsync(store, report, failed: true, cancellationToken).ConfigureAwait(false);
        await SendOwedReportAsync(store, channel, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetTerminalReportAsync(
        UpdateStateStore store,
        InstallReport report,
        bool failed,
        CancellationToken cancellationToken)
    {
        var state = store.State;
        state.ActiveWorkflow = null;
        if (failed)
        {
            state.ConsecutiveFailures = Math.Min(state.ConsecutiveFailures + 1, 31);
            state.FailedWorkflowId = report.WorkflowId;
            var baseHours = Math.Min(Math.Pow(2, state.ConsecutiveFailures - 1), 24);
            var delay = TimeSpan.FromHours(baseHours * (1 + Random.Shared.NextDouble() * 0.1));
            state.RetryAfterUtc = DateTimeOffset.UtcNow + delay;
        }
        else
        {
            state.ConsecutiveFailures = 0;
            state.RetryAfterUtc = null;
            state.FailedWorkflowId = null;
        }
        state.OwedReport = report;
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendOwedReportAsync(
        UpdateStateStore store,
        IDpsSoftwareUpdatesChannel channel,
        CancellationToken cancellationToken)
    {
        if (store.State.OwedReport is null)
            return;
        await RetryTransientAsync(
            async () =>
            {
                await DurableReportDelivery.SendAsync(store, channel, cancellationToken).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> RetryTransientAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var delaySeconds = 1.0;
        while (true)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (DpsOperationException exception) when (exception.IsRetryable)
            {
                var requested = exception.RetryAfter;
                var delay = requested ?? TimeSpan.FromSeconds(delaySeconds * (0.8 + Random.Shared.NextDouble() * 0.4));
                Console.Error.WriteLine(
                    $"Transient DPS response {exception.StatusCode} ({exception.ServiceCode ?? "unclassified"}, " +
                    $"request {exception.RequestId}, tracking {exception.TrackingId ?? "unavailable"}); retrying in {delay}.");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                if (requested is null)
                    delaySeconds = Math.Min(delaySeconds * 2, 60);
            }
        }
    }

    private static InstallReport FailureReport(
        string workflowId,
        UpdateId? installed,
        string details,
        uint extendedCode,
        int resultCode) =>
        new(workflowId, installed, new InstallResult(
            "FAILED",
            "AGENT_CORE",
            resultCode,
            extendedCode.ToString("X8"),
            details.Length <= 1024 ? details : details[..1024],
            new Dictionary<string, InstallStepResult>
            {
                ["step_0"] = new("FAILED", "AGENT_CORE", resultCode, extendedCode.ToString("X8"),
                    details.Length <= 1024 ? details : details[..1024])
            }));

    private static InstallReport TerminalReport(
        string workflowId, UpdateId? installed, string outcome, int resultCode, uint extendedCode, string details) =>
        new(workflowId, installed, new InstallResult(
            outcome,
            "NOT_APPLICABLE",
            resultCode,
            extendedCode.ToString("X8"),
            details,
            new Dictionary<string, InstallStepResult>
            {
                ["step_0"] = new(outcome, "NOT_APPLICABLE", resultCode, extendedCode.ToString("X8"), details)
            }));

    private static string? ReadConfigPath(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--config" && index + 1 < args.Length)
                return args[index + 1];
        }
        return null;
    }

    private static async Task<SoftwareUpdatesConfiguration> LoadConfigurationAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SoftwareUpdatesConfiguration>(
                   stream, ConfigJson, cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidDataException("Configuration file is empty.");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            SkittleSorter.Updater --config <absolute-path> [--check-once]

            Run the regular registered-device DPS Software Updates preview client. The
            configuration supplies a PEM device certificate, operator-trusted RSA root
            keys, state/download locations, shared provisioning lock, compatibility
            properties, and a poll interval of at least four hours. Installs use the
            fixed non-interactive root-owned installer helper.
            --check-once performs one bounded update check for a manual demo.
            """);
    }
}
