using System;

namespace AzureDpsFramework.Security
{
    /// <summary>
    /// Base security provider for device provisioning.
    /// Mirrors the Microsoft.Azure.Devices.Shared.SecurityProvider surface.
    /// </summary>
    public abstract class SecurityProvider : IDisposable
    {
        /// <summary>
        /// Gets the registration ID used during enrollment.
        /// </summary>
        public abstract string GetRegistrationID();

        /// <summary>
        /// Releases resources; implemented by derived classes.
        /// </summary>
        protected abstract void Dispose(bool disposing);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
