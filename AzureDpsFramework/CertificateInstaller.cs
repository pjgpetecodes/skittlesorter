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
            // No-op: For this sample we assume the bootstrap cert chain is trusted on the host.
            // In production, install the intermediate/root chain (from AttestationCertChainPath) into the OS trust store.
        }
    }
}
