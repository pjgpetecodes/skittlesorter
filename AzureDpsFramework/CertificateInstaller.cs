using AzureDpsFramework.Security;

namespace AzureDpsFramework
{
    /// <summary>
    /// Certificate installer stub (matching official SDK).
    /// Official SDK installs intermediate/root certs to Windows certificate store.
    /// </summary>
    internal static class CertificateInstaller
    {
        internal static void EnsureChainIsInstalled(SecurityProviderX509 securityProvider)
        {
            // No-op for now; would install certificate chain to system store
            // Not needed for symmetric key + CSR flow
        }
    }
}
