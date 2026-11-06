using AzureDpsFramework;
using AzureDpsFramework.Adr;

namespace skittle_sorter;

public static class DpsInitializationService
{
    public static async Task<UnifiedDeviceSession> InitializeAsync(IoTHubConfig iotConfig,
        string configurationPath = "appsettings.json", CancellationToken cancellationToken = default)
    {
        var configuration = DpsConfiguration.Load(configurationPath);
        var session = UnifiedDeviceSession.Create(configuration);
        try
        {
            Console.WriteLine($"[IoT] Unified SDK provisioning/connection: {configuration.RegistrationId}, {configuration.AttestationMethod}, operational certificate={configuration.RequestOperationalCertificate}");
            await session.ConnectAsync(cancellationToken);
            iotConfig.DeviceId = session.Context!.DeviceId;
            Console.WriteLine($"[IoT] Connected: Hub={session.Context.IotHubHostName}, DeviceId={iotConfig.DeviceId}");
            await TryListAdrDevicesAsync(iotConfig.DeviceId, configurationPath, cancellationToken);
            return session;
        }
        catch
        {
            session.Dispose();
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
