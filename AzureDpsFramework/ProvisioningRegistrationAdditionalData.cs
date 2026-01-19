namespace AzureDpsFramework
{
    /// <summary>
    /// Additional data for custom allocation policy webhooks.
    /// Matches Microsoft.Azure.Devices.Provisioning.Client.ProvisioningRegistrationAdditionalData.
    /// </summary>
    public class ProvisioningRegistrationAdditionalData
    {
        /// <summary>
        /// JSON data passed to custom allocation policy webhook.
        /// </summary>
        public string? JsonData { get; set; }
    }
}
