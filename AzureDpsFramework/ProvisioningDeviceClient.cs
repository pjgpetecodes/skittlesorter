using System;
using System.Threading;
using System.Threading.Tasks;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;

namespace AzureDpsFramework
{
    /// <summary>
    /// The provisioning device client for registering devices with the Azure Device Provisioning Service.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client.ProvisioningDeviceClient pattern.
    /// </summary>
    public class ProvisioningDeviceClient : IDisposable
    {
        private readonly string _globalDeviceEndpoint;
        private readonly string _idScope;
        private readonly ISecurityProvider _security;
        private readonly ITransportHandler _transport;
        private bool _disposed;

        private ProvisioningDeviceClient(
            string globalDeviceEndpoint,
            string idScope,
            ISecurityProvider security,
            ITransportHandler transport)
        {
            _globalDeviceEndpoint = globalDeviceEndpoint ?? throw new ArgumentNullException(nameof(globalDeviceEndpoint));
            _idScope = idScope ?? throw new ArgumentNullException(nameof(idScope));
            _security = security ?? throw new ArgumentNullException(nameof(security));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>
        /// Creates a new ProvisioningDeviceClient.
        /// </summary>
        /// <param name="globalDeviceEndpoint">The global device endpoint (e.g., global.azure-devices-provisioning.net).</param>
        /// <param name="idScope">The DPS ID Scope.</param>
        /// <param name="securityProvider">The security provider for authentication.</param>
        /// <param name="transport">The transport handler for communication.</param>
        /// <returns>A new ProvisioningDeviceClient instance.</returns>
        public static ProvisioningDeviceClient Create(
            string globalDeviceEndpoint,
            string idScope,
            ISecurityProvider securityProvider,
            ITransportHandler transport)
        {
            return new ProvisioningDeviceClient(globalDeviceEndpoint, idScope, securityProvider, transport);
        }

        /// <summary>
        /// Registers the device with the provisioning service.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The device registration result.</returns>
        public async Task<DeviceRegistrationResult> RegisterAsync(CancellationToken cancellationToken = default)
        {
            string? sasToken = null;
            string? csrPem = null;

            // Determine authentication method based on security provider type
            if (_security is SecurityProviderSymmetricKey symmetricKey)
            {
                // Generate SAS token for symmetric key authentication
                sasToken = DpsSasTokenGenerator.GenerateDpsSas(
                    _idScope,
                    _security.GetRegistrationId(),
                    symmetricKey.GetPrimaryKey(),
                    3600);
            }
            else if (_security is SecurityProviderX509Csr x509Csr)
            {
                // For X.509 CSR, we need to derive a symmetric key for DPS authentication
                // and provide the CSR for certificate issuance
                var enrollmentGroupKey = x509Csr.GetEnrollmentGroupKey();
                if (string.IsNullOrWhiteSpace(enrollmentGroupKey))
                {
                    throw new InvalidOperationException(
                        "X.509 CSR provisioning requires an enrollment group key for DPS authentication. " +
                        "Use SecurityProviderX509Csr.CreateFromEnrollmentGroup() to provide the enrollment group key.");
                }

                // Derive device key from enrollment group key
                var deviceKey = DpsSasTokenGenerator.DeriveDeviceKey(_security.GetRegistrationId(), enrollmentGroupKey);

                // Generate SAS token for DPS authentication
                sasToken = DpsSasTokenGenerator.GenerateDpsSas(
                    _idScope,
                    _security.GetRegistrationId(),
                    deviceKey,
                    3600);

                // Provide CSR for certificate issuance
                csrPem = x509Csr.GetCsrPem();
            }
            else
            {
                throw new NotSupportedException($"Security provider type {_security.GetType().Name} is not supported.");
            }

            // Call transport to register
            return await _transport.RegisterAsync(
                _idScope,
                _security.GetRegistrationId(),
                csrPem,
                sasToken,
                cancellationToken);
        }

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
                _security?.Dispose();
                _transport?.Dispose();
            }

            _disposed = true;
        }
    }
}
