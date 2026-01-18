namespace AzureDpsFramework
{
    /// <summary>
    /// The result of a device registration with the provisioning service.
    /// Matches the Microsoft.Azure.Devices.Provisioning.Client.DeviceRegistrationResult pattern.
    /// </summary>
    public class DeviceRegistrationResult
    {
        /// <summary>
        /// Creates a new DeviceRegistrationResult.
        /// </summary>
        public DeviceRegistrationResult(
            string? registrationId,
            string? deviceId,
            string? assignedHub,
            ProvisioningRegistrationStatusType status,
            string? substatus = null,
            string[]? issuedCertificateChain = null)
        {
            RegistrationId = registrationId;
            DeviceId = deviceId;
            AssignedHub = assignedHub;
            Status = status;
            Substatus = substatus;
            IssuedCertificateChain = issuedCertificateChain;
        }

        /// <summary>
        /// Gets the registration ID.
        /// </summary>
        public string? RegistrationId { get; }

        /// <summary>
        /// Gets the device ID assigned by the provisioning service.
        /// </summary>
        public string? DeviceId { get; }

        /// <summary>
        /// Gets the IoT Hub hostname assigned to the device.
        /// </summary>
        public string? AssignedHub { get; }

        /// <summary>
        /// Gets the registration status.
        /// </summary>
        public ProvisioningRegistrationStatusType Status { get; }

        /// <summary>
        /// Gets the substatus of the registration.
        /// </summary>
        public string? Substatus { get; }

        /// <summary>
        /// Gets the issued certificate chain (for X.509 CSR provisioning).
        /// Array of base64-encoded certificates: [device cert, intermediate CA, root CA].
        /// </summary>
        public string[]? IssuedCertificateChain { get; }
    }

    /// <summary>
    /// The registration status of a device.
    /// Matches the Microsoft.Azure.Devices.Shared.ProvisioningRegistrationStatusType enum.
    /// </summary>
    public enum ProvisioningRegistrationStatusType
    {
        /// <summary>
        /// Device has not been assigned to an IoT hub.
        /// </summary>
        Unassigned = 0,

        /// <summary>
        /// Device is in the process of being assigned to an IoT hub.
        /// </summary>
        Assigning = 1,

        /// <summary>
        /// Device has been assigned to an IoT hub.
        /// </summary>
        Assigned = 2,

        /// <summary>
        /// Device registration failed.
        /// </summary>
        Failed = 3,

        /// <summary>
        /// Device registration is disabled.
        /// </summary>
        Disabled = 4
    }
}
