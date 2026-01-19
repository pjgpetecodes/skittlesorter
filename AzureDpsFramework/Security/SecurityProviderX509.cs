using System;
using System.Security.Cryptography.X509Certificates;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// The device security provider for X.509-based authentication.
    /// Provides default registration ID extraction from certificate DNS name.
    /// </summary>
    public abstract class SecurityProviderX509 : SecurityProvider
    {
        /// <summary>
        /// Returns the registration Id extracted from the authentication certificate's DNS name.
        /// </summary>
        /// <returns>The registration Id.</returns>
        public override string GetRegistrationID()
        {
            X509Certificate2 cert = GetAuthenticationCertificate();
            return cert.GetNameInfo(X509NameType.DnsName, false);
        }

        /// <summary>
        /// Gets the certificate trust chain that will end in the Trusted Root installed on the server side.
        /// </summary>
        /// <returns>The certificate chain.</returns>
        public abstract X509Certificate2Collection GetAuthenticationCertificateChain();

        /// <summary>
        /// Gets the certificate used for TLS device authentication.
        /// </summary>
        /// <returns>The client certificate used during TLS communications.</returns>
        public abstract X509Certificate2 GetAuthenticationCertificate();
        
        protected override void Dispose(bool disposing)
        {
            // No resources to dispose in base
        }
    }
}
