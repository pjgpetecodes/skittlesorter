using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Iot.Device.Mqtt;

namespace AzureDpsFramework;

// Only coordinates ownership of the DPS connection with the independent updater.
// Authentication, MQTT packets, registration and retries belong to Microsoft's SDK.
public sealed class CoordinatedMqttClient : IMqttClient
{
    private readonly IMqttClient inner;
    private readonly string dpsHost;
    private readonly string? lockPath;
    private FileStream? lease;

    public CoordinatedMqttClient(IMqttClient inner, string dpsHost, string? lockPath)
    {
        this.inner = inner;
        this.dpsHost = dpsHost;
        this.lockPath = lockPath;
        inner.DisconnectedAsync += OnDisconnectedAsync;
    }

    public event Func<MqttPublishReceivedEventArgs, Task> PublishReceivedAsync
    {
        add => inner.PublishReceivedAsync += value;
        remove => inner.PublishReceivedAsync -= value;
    }
    public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync
    {
        add => inner.ConnectedAsync += value;
        remove => inner.ConnectedAsync -= value;
    }
    public event Func<MqttConnect, CancellationToken, Task<MqttConnect>> ConnectingAsync
    {
        add => inner.ConnectingAsync += value;
        remove => inner.ConnectingAsync -= value;
    }
    public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync
    {
        add => inner.DisconnectedAsync += value;
        remove => inner.DisconnectedAsync -= value;
    }

    public async Task<MqttConnectAck> ConnectAsync(MqttConnect connect, CancellationToken cancellationToken = default)
    {
        if (string.Equals(connect.HostName, dpsHost, StringComparison.OrdinalIgnoreCase))
            lease ??= await AcquireLockAsync(lockPath, cancellationToken);
        else
            Release();
        try
        {
            return await inner.ConnectAsync(connect, cancellationToken);
        }
        catch (Exception ex)
        {
            Release();
            Console.Error.WriteLine($"[IoT] MQTT connection to {connect.HostName} failed: {ex.GetBaseException().Message}");
            throw;
        }
    }

    public async Task DisconnectAsync(MqttDisconnect disconnect, CancellationToken cancellationToken = default)
    {
        try { await inner.DisconnectAsync(disconnect, cancellationToken); }
        finally { Release(); }
    }

    public Task<MqttPublishAck> PublishAsync(MqttPublish publish, CancellationToken cancellationToken = default) =>
        inner.PublishAsync(publish, cancellationToken);
    public Task<MqttSubscribeAck> SubscribeAsync(MqttSubscribe subscribe, CancellationToken cancellationToken = default) =>
        inner.SubscribeAsync(subscribe, cancellationToken);
    public Task<MqttUnsubscribeAck> UnsubscribeAsync(MqttUnsubscribe unsubscribe, CancellationToken cancellationToken = default) =>
        inner.UnsubscribeAsync(unsubscribe, cancellationToken);
    public bool IsConnected() => inner.IsConnected();

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        Release();
        return Task.CompletedTask;
    }

    private void Release() => Interlocked.Exchange(ref lease, null)?.Dispose();

    public static async Task<FileStream?> AcquireLockAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(250, timeout.Token); }
        }
    }

    public void Dispose()
    {
        try { inner.Dispose(); }
        finally { Release(); }
    }
}
