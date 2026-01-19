using System;
using System.Threading;
using System.Threading.Tasks;
using AzureDpsFramework.Security;
using AzureDpsFramework.Transport;

namespace AzureDpsFramework
{
    /// <summary>
    /// Allows devices to use the Device Provisioning Service.
    /// Aligned with Microsoft.Azure.Devices.Provisioning.Client.ProvisioningDeviceClient pattern.
    /// 
    /// PREVIEW EXTENSION: Adds support for X.509 CSR-based provisioning via Azure Device Registry (ADR).
    /// This allows devices to request certificate issuance during DPS provisioning using the 2025-07-01-preview API.
    /// </summary>
    public class ProvisioningDeviceClient : IDisposable
    {
        private readonly string _globalDeviceEndpoint;
        private readonly string _idScope;
        private readonly SecurityProvider _security;
        private readonly ProvisioningTransportHandler _transport;
        private bool _disposed;

        private ProvisioningDeviceClient(
            string globalDeviceEndpoint,
            string idScope,
            SecurityProvider securityProvider,
            ProvisioningTransportHandler transport)
        {
            _globalDeviceEndpoint = globalDeviceEndpoint ?? throw new ArgumentNullException(nameof(globalDeviceEndpoint));
            _idScope = idScope ?? throw new ArgumentNullException(nameof(idScope));
            _security = securityProvider ?? throw new ArgumentNullException(nameof(securityProvider));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

            // Logging stubs (matching official SDK pattern)
            Logging.Associate(this, _security);
            Logging.Associate(this, _transport);
        }

        /// <summary>
        /// Creates an instance of the Device Provisioning Client.
        /// </summary>
        /// <param name="globalDeviceEndpoint">The GlobalDeviceEndpoint for the Device Provisioning Service.</param>
        /// <param name="idScope">The IDScope for the Device Provisioning Service.</param>
        /// <param name="securityProvider">The security provider instance.</param>
        /// <param name="transport">The type of transport (e.g. HTTP, AMQP, MQTT).</param>
        /// <returns>An instance of the ProvisioningDeviceClient</returns>
        public static ProvisioningDeviceClient Create(
            string globalDeviceEndpoint,
            string idScope,
            SecurityProvider securityProvider,
            ProvisioningTransportHandler transport)
        {
            // Certificate installer stub for X509 (official SDK does this)
            // We don't use X509 auth for DPS in this preview implementation
            if (securityProvider is SecurityProviderX509 x509SecurityProvider)
            {
                CertificateInstaller.EnsureChainIsInstalled(x509SecurityProvider);
            }

            return new ProvisioningDeviceClient(globalDeviceEndpoint, idScope, securityProvider, transport);
        }

        /// <summary>
        /// Stores product information that will be appended to the user agent string that is sent to IoT hub.
        /// </summary>
        public string? ProductInfo { get; set; }

        /// <summary>
        /// Registers the current device using the Device Provisioning Service and assigns it to an IoT hub.
        /// </summary>
        /// <param name="timeout">The maximum amount of time to allow this operation to run for before timing out.</param>
        /// <returns>The registration result.</returns>
        public Task<DeviceRegistrationResult> RegisterAsync(TimeSpan timeout)
        {
            return RegisterAsync(null, timeout);
        }

        /// <summary>
        /// Registers the current device using the Device Provisioning Service and assigns it to an IoT hub.
        /// </summary>
        /// <param name="data">
        /// The optional additional data that is passed through to the custom allocation policy webhook if
        /// a custom allocation policy webhook is setup for this enrollment.
        /// </param>
        /// <param name="timeout">The maximum amount of time to allow this operation to run for before timing out.</param>
        /// <returns>The registration result.</returns>
        public Task<DeviceRegistrationResult> RegisterAsync(ProvisioningRegistrationAdditionalData? data, TimeSpan timeout)
        {
            var cts = new CancellationTokenSource(timeout);
            return RegisterAsync(data, cts.Token);
        }

        /// <summary>
        /// Registers the current device using the Device Provisioning Service and assigns it to an IoT hub.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The registration result.</returns>
        public Task<DeviceRegistrationResult> RegisterAsync(CancellationToken cancellationToken = default)
        {
            Logging.RegisterAsync(this, _globalDeviceEndpoint, _idScope, _transport, _security);
            return RegisterAsync(null, cancellationToken);
        }

        /// <summary>
        /// Registers the current device using the Device Provisioning Service and assigns it to an IoT hub.
        /// </summary>
        /// <param name="data">
        /// The optional additional data that is passed through to the custom allocation policy webhook if
        /// a custom allocation policy webhook is setup for this enrollment.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The registration result.</returns>
        public async Task<DeviceRegistrationResult> RegisterAsync(
            ProvisioningRegistrationAdditionalData? data,
            CancellationToken cancellationToken = default)
        {
            Logging.RegisterAsync(this, _globalDeviceEndpoint, _idScope, _transport, _security);

            if (data != null)
            {
                throw new NotImplementedException(
                    "Custom allocation policy additional data is not yet supported in this preview implementation.");
            }

            string? sasToken = null;
            string? csrPem = null;

            // ========== PREVIEW EXTENSION: CSR-based provisioning ==========
            // The official SDK delegates authentication to the transport layer by passing
            // the SecurityProvider in ProvisioningTransportRegisterMessage.
            // 
            // This preview implementation explicitly handles two authentication flows:
            // 1. Standard symmetric key authentication
            // 2. CSR-based provisioning (PREVIEW): Uses symmetric key for DPS auth + CSR for cert issuance
            // 
            // The CSR flow is unique to Azure Device Registry (ADR) and not yet in the official SDK.
            // ================================================================
            
            if (_security is SecurityProviderSymmetricKey symmetricKey)
            {
                // Standard symmetric key authentication (same as official SDK)
                sasToken = DpsSasTokenGenerator.GenerateDpsSas(
                    _idScope,
                        _security.GetRegistrationID(),
                    symmetricKey.GetPrimaryKey(),
                    3600);
            }
            else if (_security is SecurityProviderX509Csr x509Csr)
            {
                // ========== PREVIEW: CSR-based certificate issuance via ADR ==========
                // This is NEW functionality not present in the official SDK.
                // 
                // Flow:
                // 1. Derive a symmetric key from the enrollment group key for DPS authentication
                // 2. Generate SAS token using the derived key (authenticates to DPS)
                // 3. Include CSR in the registration payload (requests cert issuance from ADR)
                // 4. DPS provisions the device AND issues a certificate chain
                // 
                // This enables zero-touch certificate provisioning for IoT devices.
                // =======================================================================
                var enrollmentGroupKey = x509Csr.GetEnrollmentGroupKey();
                if (string.IsNullOrWhiteSpace(enrollmentGroupKey))
                {
                    throw new InvalidOperationException(
                        "X.509 CSR provisioning requires an enrollment group key for DPS authentication. " +
                        "Use SecurityProviderX509Csr.CreateFromEnrollmentGroup() to provide the enrollment group key.");
                }

                // Derive device key from enrollment group key
                var deviceKey = DpsSasTokenGenerator.DeriveDeviceKey(_security.GetRegistrationID(), enrollmentGroupKey);

                // Generate SAS token for DPS authentication
                sasToken = DpsSasTokenGenerator.GenerateDpsSas(
                    _idScope,
                    _security.GetRegistrationID(),
                    deviceKey,
                    3600);

                // Provide CSR for certificate issuance
                csrPem = x509Csr.GetCsrPem();
            }
            else
            {
                throw new NotSupportedException($"Security provider type {_security.GetType().Name} is not supported.");
            }

            // ========== ARCHITECTURAL NOTE ==========
            // Official SDK: Passes SecurityProvider in the message; transport extracts credentials.
            // This implementation: Pre-computes SAS token and CSR; passes primitives to transport.
            // 
            // Rationale: Makes the CSR preview flow explicit and easier to understand.
            // When Microsoft adds official CSR support, we can align with their approach.
            // =========================================
            var message = new ProvisioningTransportRegisterMessage(
                _globalDeviceEndpoint,
                _idScope,
                csrPem,
                sasToken,
                ProductInfo);

            return await _transport.RegisterAsync(message, cancellationToken);
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
