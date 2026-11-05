using System.Security.Cryptography.X509Certificates;

namespace AzureDpsFramework.Security;

public sealed class SecurityProviderX509Certificate : SecurityProviderX509
{
    private readonly string _registrationId;
    private readonly X509Certificate2 _certificate;
    private readonly X509Certificate2Collection _chain;

    public SecurityProviderX509Certificate(string registrationId, X509Certificate2 certificate,
        X509Certificate2Collection? chain = null)
    {
        _registrationId = registrationId;
        _certificate = certificate;
        _chain = chain ?? new X509Certificate2Collection();
    }

    public override string GetRegistrationID() => _registrationId;
    public override X509Certificate2 GetAuthenticationCertificate() => _certificate;
    public override X509Certificate2Collection GetAuthenticationCertificateChain() => _chain;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificate.Dispose();
            foreach (var certificate in _chain) certificate.Dispose();
        }
    }
}
