using System;
using System.IO;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// Security provider for X.509 Certificate Signing Request (CSR) based provisioning.
    /// This provider is used when requesting DPS to issue a certificate via Azure Device Registry.
    /// </summary>
    public class SecurityProviderX509Csr : ISecurityProvider
    {
        private readonly string _registrationId;
        private readonly string _csrPem;
        private readonly string _privateKeyPem;
        private readonly string? _enrollmentGroupKey;
        private bool _disposed;

        /// <summary>
        /// Creates a new SecurityProviderX509Csr with the specified CSR and private key.
        /// </summary>
        /// <param name="registrationId">The registration ID for the device.</param>
        /// <param name="csrPem">The certificate signing request in PEM format.</param>
        /// <param name="privateKeyPem">The private key in PEM format.</param>
        /// <param name="enrollmentGroupKey">The enrollment group key (optional, required for DPS authentication with CSR).</param>
        public SecurityProviderX509Csr(string registrationId, string csrPem, string privateKeyPem, string? enrollmentGroupKey = null)
        {
            _registrationId = registrationId ?? throw new ArgumentNullException(nameof(registrationId));
            _csrPem = csrPem ?? throw new ArgumentNullException(nameof(csrPem));
            _privateKeyPem = privateKeyPem ?? throw new ArgumentNullException(nameof(privateKeyPem));
            _enrollmentGroupKey = enrollmentGroupKey;
        }

        /// <summary>
        /// Creates a new SecurityProviderX509Csr by reading CSR and private key from files.
        /// </summary>
        /// <param name="registrationId">The registration ID for the device.</param>
        /// <param name="csrFilePath">Path to the CSR file.</param>
        /// <param name="privateKeyFilePath">Path to the private key file.</param>
        /// <param name="enrollmentGroupKey">The enrollment group key (optional, required for DPS authentication with CSR).</param>
        public static SecurityProviderX509Csr CreateFromFiles(string registrationId, string csrFilePath, string privateKeyFilePath, string? enrollmentGroupKey = null)
        {
            var csrPem = File.ReadAllText(csrFilePath);
            var privateKeyPem = File.ReadAllText(privateKeyFilePath);
            return new SecurityProviderX509Csr(registrationId, csrPem, privateKeyPem, enrollmentGroupKey);
        }

        /// <summary>
        /// Creates a new SecurityProviderX509Csr with enrollment group key for DPS authentication.
        /// </summary>
        /// <param name="registrationId">The registration ID for the device.</param>
        /// <param name="csrPem">The certificate signing request in PEM format.</param>
        /// <param name="privateKeyPem">The private key in PEM format.</param>
        /// <param name="enrollmentGroupKey">The enrollment group primary key (base64 encoded).</param>
        public static SecurityProviderX509Csr CreateFromEnrollmentGroup(string registrationId, string csrPem, string privateKeyPem, string enrollmentGroupKey)
        {
            return new SecurityProviderX509Csr(registrationId, csrPem, privateKeyPem, enrollmentGroupKey);
        }

        /// <summary>
        /// Gets the registration ID for this device.
        /// </summary>
        public string GetRegistrationId() => _registrationId;

        /// <summary>
        /// Gets the certificate signing request in PEM format.
        /// </summary>
        public string GetCsrPem() => _csrPem;

        /// <summary>
        /// Gets the private key in PEM format.
        /// </summary>
        public string GetPrivateKeyPem() => _privateKeyPem;

        /// <summary>
        /// Gets the enrollment group key, if provided.
        /// </summary>
        public string? GetEnrollmentGroupKey() => _enrollmentGroupKey;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Clear sensitive data from memory
                // Note: Strings are immutable in .NET, so this is best effort
            }

            _disposed = true;
        }
    }
}
