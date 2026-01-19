using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;

namespace AzureDpsFramework
{
    /// <summary>
    /// Logging stub (matching official SDK pattern).
    /// In a production implementation, this would integrate with ILogger or similar.
    /// </summary>
    internal static class Logging
    {
        internal static void Associate(object source, object target)
        {
            // No-op stub: Official SDK uses this for diagnostic correlation
            // In production, would log association between components
        }

        internal static void RegisterAsync(
            ProvisioningDeviceClient client,
            string endpoint,
            string idScope,
            ProvisioningTransportHandler transport,
            SecurityProvider security)
        {
            // No-op stub: Official SDK logs registration initiation
            // In production, would log DPS registration attempt with details
        }
    }
}
