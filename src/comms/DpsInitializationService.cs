using AzureDpsFramework;
using AzureDpsFramework.Adr;
using Microsoft.Azure.Iot.Device.MqttNetAdapter;
using Microsoft.Azure.Iot.Device.MQTTnetAdapter;
using Microsoft.Azure.Iot.Device.Unified.Connection;
using Microsoft.Azure.Iot.Device.Unified.Models;
using Microsoft.Azure.Iot.Device.Unified.Telemetry;

namespace skittle_sorter;

public static class DpsInitializationService
{
    public static async Task<(ConnectionClient Connection, TelemetryClient Telemetry, CoordinatedMqttClient Mqtt, DeviceProvisioningIdentity Identity)> InitializeAsync(IoTHubConfig iotConfig,
        string configurationPath = "appsettings.json", CancellationToken cancellationToken = default)
    {
        var configuration = DpsConfiguration.Load(configurationPath);
        var identity = DeviceProvisioningIdentity.Create(configuration);
        CoordinatedMqttClient? mqtt = null;
        ConnectionClient? connection = null;
        TelemetryClient? telemetry = null;
        try
        {
            mqtt = new CoordinatedMqttClient(new MqttNetClient(new MqttNetClientOptions
            {
                EnableMqttLogs = configuration.EnableDebugLogging
            }), configuration.ProvisioningHost, configuration.ProvisioningLockPath);
            connection = new ConnectionClient(new ConnectionClientOptions
            {
                MqttClient = mqtt,
                EnableMqttLogging = configuration.EnableDebugLogging,
                EnableTwin = false,
                EnableDirectMethods = false,
                EnableCloudToDeviceMessages = false
            }, identity.LoadContext());
            telemetry = new TelemetryClient(connection);
            Console.WriteLine($"[IoT] Unified SDK provisioning/connection: {configuration.RegistrationId}, {configuration.AttestationMethod}, operational certificate={configuration.RequestOperationalCertificate}");
            using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var connecting = connection.ProvisionAndConnectAsync(identity.Provisioning, identity.Authentication, cancellationToken: connectCancellation.Token);
            if (await Task.WhenAny(connecting, identity.CredentialFailure) == identity.CredentialFailure)
                connectCancellation.Cancel();
            Microsoft.Azure.Iot.Device.Models.ConnectionContext context;
            try { context = await connecting; }
            catch (Exception ex) when (identity.CredentialFailure.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(identity.CredentialFailure.Result.Message, identity.CredentialFailure.Result.InnerException ?? ex);
            }
            catch (Exception)
            {
                var current = connection.GetCurrentConnectionContext();
                Console.Error.WriteLine($"[IoT] Connection failed. Assigned Hub={current?.IotHubHostName ?? "(none)"}, profile={current?.ConnectionProfile}, issued certificate count={current?.IssuedClientCertificates?.Count ?? 0}.");
                throw;
            }
            identity.SaveContext(context);
            iotConfig.DeviceId = context.DeviceId;
            Console.WriteLine($"[IoT] Connected: Hub={context.IotHubHostName}, DeviceId={iotConfig.DeviceId}");
            await TryListAdrDevicesAsync(iotConfig.DeviceId, configurationPath, cancellationToken);
            return (connection, telemetry, mqtt, identity);
        }
        catch
        {
            telemetry?.Dispose();
            connection?.Dispose();
            mqtt?.Dispose();
            identity.Dispose();
            throw;
        }
    }

    private static async Task TryListAdrDevicesAsync(string deviceName, string configurationPath, CancellationToken cancellationToken)
    {
        try
        {
            var configuration = AdrConfiguration.Load(configurationPath);
            if (!configuration.IsConfigured()) return;
            using var client = new AdrDeviceRegistryClient();
            var devices = await client.ListDevicesAsync(configuration.SubscriptionId!,
                configuration.ResourceGroupName!, configuration.NamespaceName!, cancellationToken);
            Console.WriteLine($"[ADR] Devices in namespace '{configuration.NamespaceName}': {devices.Count}");
            foreach (var device in devices.Take(5)) Console.WriteLine($"[ADR] - {device.Name} ({device.Id})");
            var update = AdrDeviceUpdateConfiguration.Load(configurationPath);
            if (update.Enabled)
                TryUpdateAdrDeviceAttributes(deviceName, update.Attributes ?? new Dictionary<string, object>(),
                    update.Tags, update.DeviceEnabled, update.OperatingSystemVersion, configurationPath, cancellationToken);
            var details = await client.GetDeviceAsync(configuration.SubscriptionId!, configuration.ResourceGroupName!,
                configuration.NamespaceName!, deviceName, cancellationToken);
            Console.WriteLine(details == null
                ? $"[ADR] Device '{deviceName}' not found."
                : $"[ADR] Device '{details.Name}': {details.Properties}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { Console.WriteLine($"[ADR] Listing failed: {ex.Message}"); }
    }

    public static bool TryUpdateAdrDeviceAttributes(string deviceName, IDictionary<string, object> attributes,
        IDictionary<string, string>? tags = null, bool? enabled = null, string? operatingSystemVersion = null,
        string configurationPath = "appsettings.json", CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = AdrConfiguration.Load(configurationPath);
            if (!configuration.IsConfigured())
            {
                Console.WriteLine("[ADR] Update skipped: ADR configuration is not set.");
                return false;
            }
            using var client = new AdrDeviceRegistryClient();
            var updated = client.UpdateDeviceAsync(configuration.SubscriptionId!, configuration.ResourceGroupName!,
                configuration.NamespaceName!, deviceName, attributes, enabled, tags, operatingSystemVersion, cancellationToken).GetAwaiter().GetResult();
            Console.WriteLine(updated == null ? $"[ADR] Update returned no content for '{deviceName}'." : $"[ADR] Updated '{updated.Name}'.");
            return updated != null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Console.WriteLine($"[ADR] Update failed: {ex.Message}");
            return false;
        }
    }
}
