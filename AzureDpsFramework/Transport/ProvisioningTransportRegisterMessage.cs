namespace AzureDpsFramework.Transport
{
    /// <summary>
    /// Registration message wrapper for provisioning transport.
    /// Mirrors the official SDK shape while carrying the data our MQTT handler needs.
    /// </summary>
    public sealed class ProvisioningTransportRegisterMessage
    {
        public ProvisioningTransportRegisterMessage(
            string globalDeviceEndpoint,
            string idScope,
            string? csrPem,
            string? sasToken,
            string? productInfo,
            Security.SecurityProvider security)
        {
            GlobalDeviceEndpoint = globalDeviceEndpoint;
            IdScope = idScope;
            CsrPem = csrPem;
            SasToken = sasToken;
            ProductInfo = productInfo;
            Security = security;
        }

        public string GlobalDeviceEndpoint { get; }
        public string IdScope { get; }
        public string? CsrPem { get; }
        public string? SasToken { get; }
        public string? ProductInfo { get; }
        public Security.SecurityProvider Security { get; }
    }
}
