using System.Security.Cryptography.X509Certificates;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// Security provider for CSR-based certificate issuance using X.509 certificate attestation.
    /// 
    /// PREVIEW: Supports 2025-07-01-preview API for CSR submission to Azure Device Registry (ADR).
    /// 
    /// This provider uses an existing X.509 certificate for DPS authentication while submitting
    /// a Certificate Signing Request (CSR) to receive a new device certificate from the service.
    /// </summary>
    public class SecurityProviderX509CsrWithCert : SecurityProviderX509
    {
        private readonly X509Certificate2 _authenticationCertificate;
        private readonly X509Certificate2Collection? _authenticationCertificateChain;
        private readonly string _registrationId;
        private readonly string _csrPem;
        private readonly string _keyPem;

        /// <summary>
        /// Initializes a new instance of the SecurityProviderX509CsrWithCert class.
        /// </summary>
        /// <param name="authenticationCertificate">
        /// The existing X.509 certificate used for DPS authentication. Must include private key.
        /// </param>
        /// <param name="registrationId">
        /// The registration ID for the device. If null, extracts from authentication certificate DNS name.
        /// </param>
        /// <param name="csrPem">
        /// The Certificate Signing Request in PEM format to submit to DPS for certificate issuance.
        /// </param>
        /// <param name="keyPem">
        /// The private key (PKCS#8 format) corresponding to the CSR, in PEM format.
        /// </param>
        /// <param name="authenticationCertificateChain">
        /// Optional certificate chain for the authentication certificate.
        /// </param>
        public SecurityProviderX509CsrWithCert(
            X509Certificate2 authenticationCertificate,
            string registrationId,
            string csrPem,
            string keyPem,
            X509Certificate2Collection? authenticationCertificateChain = null)
        {
            _authenticationCertificate = authenticationCertificate ?? throw new ArgumentNullException(nameof(authenticationCertificate));
            _registrationId = registrationId;
            _csrPem = csrPem ?? throw new ArgumentNullException(nameof(csrPem));
            _keyPem = keyPem ?? throw new ArgumentNullException(nameof(keyPem));
            _authenticationCertificateChain = authenticationCertificateChain;
        }

        /// <summary>
        /// Gets the Certificate Signing Request in PEM format.
        /// PREVIEW: This CSR is submitted to DPS using the 2025-07-01-preview API.
        /// </summary>
        public string CsrPem => _csrPem;

        /// <summary>
        /// Gets the private key in PEM format (PKCS#8) for the requested certificate.
        /// </summary>
        public string KeyPem => _keyPem;

        /// <summary>
        /// Gets the registration ID. If provided explicitly, returns that value.
        /// Otherwise, extracts from the authentication certificate's DNS name.
        /// </summary>
        public override string GetRegistrationID()
        {
            if (!string.IsNullOrEmpty(_registrationId))
            {
                return _registrationId;
            }

            // Fallback: extract from authentication certificate
            return base.GetRegistrationID();
        }

        /// <summary>
        /// Gets the existing X.509 certificate used for DPS authentication.
        /// This is NOT the certificate being requested via CSR.
        /// </summary>
        public override X509Certificate2 GetAuthenticationCertificate()
        {
            return _authenticationCertificate;
        }

        /// <summary>
        /// Gets the certificate chain for the authentication certificate.
        /// </summary>
        public override X509Certificate2Collection GetAuthenticationCertificateChain()
        {
            return _authenticationCertificateChain ?? new X509Certificate2Collection();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _authenticationCertificate?.Dispose();
            }
        }
    }
}
