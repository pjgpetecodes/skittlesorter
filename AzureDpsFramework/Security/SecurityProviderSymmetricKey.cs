using System;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// Security provider for symmetric key based provisioning.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client.SecurityProviderSymmetricKey pattern.
    /// </summary>
    public class SecurityProviderSymmetricKey : ISecurityProvider
    {
        private readonly string _registrationId;
        private readonly string _primaryKey;
        private readonly string? _secondaryKey;
        private bool _disposed;

        /// <summary>
        /// Creates a new SecurityProviderSymmetricKey.
        /// </summary>
        /// <param name="registrationId">The registration ID for the device.</param>
        /// <param name="primaryKey">The primary symmetric key (base64 encoded).</param>
        /// <param name="secondaryKey">The secondary symmetric key (base64 encoded), optional.</param>
        public SecurityProviderSymmetricKey(string registrationId, string primaryKey, string? secondaryKey = null)
        {
            _registrationId = registrationId ?? throw new ArgumentNullException(nameof(registrationId));
            _primaryKey = primaryKey ?? throw new ArgumentNullException(nameof(primaryKey));
            _secondaryKey = secondaryKey;
        }

        /// <summary>
        /// Creates a SecurityProviderSymmetricKey with a device key derived from an enrollment group key.
        /// </summary>
        /// <param name="registrationId">The registration ID for the device.</param>
        /// <param name="enrollmentGroupKey">The enrollment group primary key (base64 encoded).</param>
        /// <returns>A new SecurityProviderSymmetricKey with the derived device key.</returns>
        public static SecurityProviderSymmetricKey CreateFromEnrollmentGroupKey(string registrationId, string enrollmentGroupKey)
        {
            var derivedKey = DpsSasTokenGenerator.DeriveDeviceKey(registrationId, enrollmentGroupKey);
            return new SecurityProviderSymmetricKey(registrationId, derivedKey);
        }

        /// <summary>
        /// Gets the registration ID for this device.
        /// </summary>
        public string GetRegistrationId() => _registrationId;

        /// <summary>
        /// Gets the primary symmetric key.
        /// </summary>
        public string GetPrimaryKey() => _primaryKey;

        /// <summary>
        /// Gets the secondary symmetric key, if available.
        /// </summary>
        public string? GetSecondaryKey() => _secondaryKey;

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
