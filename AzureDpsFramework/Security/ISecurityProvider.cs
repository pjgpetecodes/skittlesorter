using System;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// Provides security information for device provisioning.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client security provider pattern.
    /// </summary>
    public interface ISecurityProvider : IDisposable
    {
        /// <summary>
        /// Gets the registration ID for this device.
        /// </summary>
        string GetRegistrationId();
    }
}
