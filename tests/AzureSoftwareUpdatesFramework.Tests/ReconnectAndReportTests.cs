using AzureSoftwareUpdatesFramework;
using Xunit;

namespace AzureSoftwareUpdatesFramework.Tests;

public sealed class ReconnectAndReportTests
{
    [Fact]
    public async Task LostReportSessionIsRecreatedAndExactDurableReportReplayed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "reconnect-tests", Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var report = new InstallReport("workflow-1", new UpdateId("sorter", "app", "1.1"),
                new InstallResult("SUCCEEDED", "NOT_APPLICABLE", 700, "00000000", null, null));
            var store = new UpdateStateStore(path);
            store.State.OwedReport = report;
            await store.SaveAsync();
            var first = new FakeChannel { ReportFailure = new IOException("connection lost after publish") };
            var second = new FakeChannel();
            var sessions = new Queue<FakeChannel>(new[] { first, second });
            await using var reconnecting = new ReconnectingDpsSoftwareUpdatesChannel(_ =>
                Task.FromResult<IDpsSoftwareUpdatesChannel>(sessions.Dequeue()));

            await DurableReportDelivery.SendAsync(store, reconnecting, CancellationToken.None);

            Assert.True(first.Disposed);
            Assert.Same(report, Assert.Single(first.Reports));
            Assert.Same(report, Assert.Single(second.Reports));
            Assert.Null(new UpdateStateStore(path).State.OwedReport);
        }
        finally
        {
            DeleteStateDirectory(path);
        }
    }

    [Fact]
    public async Task ReportConflictPreservesExactPersistedReportAndDoesNotReconnect()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "reconnect-tests", Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var store = new UpdateStateStore(path);
            store.State.OwedReport = new InstallReport("workflow-conflict", null,
                new InstallResult("FAILED", "AGENT_CORE", -5, "50000000", "installer failed", null));
            await store.SaveAsync();
            var session = new FakeChannel
            {
                ReportFailure = new DpsOperationException(
                    409, 409000, "REPORT_CONFLICT", "1", "tracking", null, "Conflict")
            };
            var connectCount = 0;
            await using var reconnecting = new ReconnectingDpsSoftwareUpdatesChannel(_ =>
            {
                connectCount++;
                return Task.FromResult<IDpsSoftwareUpdatesChannel>(session);
            });

            await Assert.ThrowsAsync<DpsOperationException>(() =>
                DurableReportDelivery.SendAsync(store, reconnecting, CancellationToken.None));

            Assert.Equal(1, connectCount);
            Assert.NotNull(store.State.OwedReport);
            Assert.Equal("workflow-conflict", new UpdateStateStore(path).State.OwedReport!.WorkflowId);
            Assert.Equal("FAILED", new UpdateStateStore(path).State.OwedReport!.InstallResult.Outcome);
        }
        finally
        {
            DeleteStateDirectory(path);
        }
    }

    [Fact]
    public async Task CompletedCheckClosesSessionAndNextCheckUsesFreshSession()
    {
        var first = new FakeChannel();
        var second = new FakeChannel();
        var sessions = new Queue<FakeChannel>(new[] { first, second });
        await using var reconnecting = new ReconnectingDpsSoftwareUpdatesChannel(_ =>
            Task.FromResult<IDpsSoftwareUpdatesChannel>(sessions.Dequeue()));

        await reconnecting.CheckForUpdateAsync(CancellationToken.None);
        var response = await reconnecting.CheckForUpdateAsync(CancellationToken.None);

        Assert.True(first.Disposed);
        Assert.Null(response.Update);
        Assert.Equal(1, second.CheckCount);
    }

    [Fact]
    public async Task SharedLockIsHeldOnlyDuringExchangeAndDisconnectedBeforeUnlocking()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "lease-tests", Guid.NewGuid().ToString("N"));
        var lockPath = Path.Combine(directory, "provisioning.lock");
        Directory.CreateDirectory(directory);
        var sessions = new List<FakeChannel>();
        try
        {
            await using var channel = new ReconnectingDpsSoftwareUpdatesChannel(_ =>
            {
                var session = new FakeChannel
                {
                    OnCheck = () =>
                    {
                        Assert.Throws<IOException>(() =>
                        {
                            using var competing = new FileStream(
                                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        });
                    },
                    OnDispose = () =>
                    {
                        Assert.Throws<IOException>(() =>
                        {
                            using var competing = new FileStream(
                                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        });
                    }
                };
                sessions.Add(session);
                return Task.FromResult<IDpsSoftwareUpdatesChannel>(session);
            }, async token => await ProvisioningSessionLock.AcquireAsync(
                lockPath, TimeSpan.FromSeconds(1), token));

            await channel.CheckForUpdateAsync(CancellationToken.None);
            Assert.True(sessions[0].Disposed);
            using (var sorterProvisioning = new FileStream(
                       lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                // This represents the installer/sorter restart or poll wait, after the exchange.
                Assert.True(sorterProvisioning.CanWrite);
            }
            await channel.CheckForUpdateAsync(CancellationToken.None);
            Assert.Equal(2, sessions.Count);
            Assert.All(sessions, session => Assert.True(session.Disposed));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DeleteStateDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class FakeChannel : IDpsSoftwareUpdatesChannel
    {
        public Exception? ReportFailure { get; init; }
        public Exception? CheckFailure { get; set; }
        public List<InstallReport> Reports { get; } = new();
        public bool Disposed { get; private set; }
        public int CheckCount { get; private set; }
        public Action? OnCheck { get; init; }
        public Action? OnDispose { get; init; }

        public Task<UpdateCheckResponse> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            OnCheck?.Invoke();
            return CheckFailure is { } failure
                ? Task.FromException<UpdateCheckResponse>(failure)
                : Task.FromResult(new UpdateCheckResponse(null, null, null));
        }

        public Task ReportAsync(InstallReport report, CancellationToken cancellationToken)
        {
            Reports.Add(report);
            return ReportFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            OnDispose?.Invoke();
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
