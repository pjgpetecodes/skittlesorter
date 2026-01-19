namespace AzureDpsFramework.Transport
{
    /// <summary>
    /// Registration message wrapper for provisioning transport.
    /// Mirrors the official SDK shape while carrying the data our MQTT handler needs.
    /// </summary>
    public sealed class ProvisioningTransportRegisterMessage
    {
        public ProvisioningTransportRegisterMessage(
            string idScope,
            string registrationId,
            string? csrPem,
            string? sasToken,
            string? productInfo = null)
        {
            IdScope = idScope;
            RegistrationId = registrationId;
            CsrPem = csrPem;
            SasToken = sasToken;
            ProductInfo = productInfo;
        }

        public string IdScope { get; }
        public string RegistrationId { get; }
        public string? CsrPem { get; }
        public string? SasToken { get; }
        public string? ProductInfo { get; }
    }
}
