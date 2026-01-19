using System;
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
            // Not implemented: Would install certificate chain to system store
            // Not needed for symmetric key + CSR flow in this preview
            throw new NotImplementedException(
                "X.509 certificate chain installation is not implemented. " +
                "This preview implementation focuses on symmetric key authentication with CSR-based certificate issuance.");
        }
    }
}
