namespace AzureSoftwareUpdatesFramework;

/// <summary>Recreates a lost DPS session; durable report ownership stays with the caller.</summary>
public sealed class ReconnectingDpsSoftwareUpdatesChannel : IDpsSoftwareUpdatesChannel
{
    private readonly Func<CancellationToken, Task<IDpsSoftwareUpdatesChannel>> _connect;
    private readonly Func<CancellationToken, Task<IAsyncDisposable>>? _acquireSessionLease;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDpsSoftwareUpdatesChannel? _channel;
    private bool _disposed;

    public ReconnectingDpsSoftwareUpdatesChannel(
        Func<CancellationToken, Task<IDpsSoftwareUpdatesChannel>> connect,
        Func<CancellationToken, Task<IAsyncDisposable>>? acquireSessionLease = null)
    {
        _connect = connect;
        _acquireSessionLease = acquireSessionLease;
    }

    public Task<UpdateCheckResponse> CheckForUpdateAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(channel => channel.CheckForUpdateAsync(cancellationToken), cancellationToken);

    public Task ReportAsync(InstallReport report, CancellationToken cancellationToken) =>
        ExecuteAsync(async channel =>
        {
            await channel.ReportAsync(report, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private async Task<T> ExecuteAsync<T>(
        Func<IDpsSoftwareUpdatesChannel, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IAsyncDisposable? lease = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_acquireSessionLease is not null)
                lease = await _acquireSessionLease(cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    _channel ??= await _connect(cancellationToken).ConfigureAwait(false);
                    return await operation(_channel).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or TimeoutException)
                {
                    await DropSessionAsync().ConfigureAwait(false);
                    if (attempt >= 3)
                        throw;
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            try
            {
                // Disconnect before unlocking: another process may reuse this same DPS client id.
                await DropSessionAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (lease is not null)
                        await lease.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }

    private async Task DropSessionAsync()
    {
        var channel = _channel;
        _channel = null;
        if (channel is null)
            return;
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or
                                           MQTTnet.Exceptions.MqttCommunicationException)
        {
            // A failed disconnect must not keep the dead session for the next operation.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await DropSessionAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}

public static class DurableReportDelivery
{
    public static async Task SendAsync(
        UpdateStateStore store,
        IDpsSoftwareUpdatesChannel channel,
        CancellationToken cancellationToken)
    {
        if (store.State.OwedReport is not { } report)
            return;
        // Clear only after an acknowledgement. In particular a 409 preserves the exact report.
        await channel.ReportAsync(report, cancellationToken).ConfigureAwait(false);
        store.State.OwedReport = null;
        await store.SaveAsync(cancellationToken).ConfigureAwait(false);
    }
}
