using System;
using System.Threading;
using System.Threading.Tasks;

namespace AzureDpsFramework.Transport
{
    /// <summary>
    /// Represents a transport handler for device provisioning.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client transport pattern.
    /// </summary>
    public interface ITransportHandler : IDisposable
    {
        /// <summary>
        /// Registers the device with the provisioning service.
        /// </summary>
        /// <param name="idScope">The DPS ID Scope.</param>
        /// <param name="registrationId">The device registration ID.</param>
        /// <param name="csrPem">The certificate signing request in PEM format (optional, for X.509 CSR provisioning).</param>
        /// <param name="sasToken">The SAS token for authentication (for symmetric key provisioning).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The device registration result.</returns>
        Task<DeviceRegistrationResult> RegisterAsync(
            string idScope,
            string registrationId,
            string? csrPem,
            string? sasToken,
            CancellationToken cancellationToken);
    }
}
