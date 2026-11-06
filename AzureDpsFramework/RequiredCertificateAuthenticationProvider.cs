using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Iot.Device.Authentication;

namespace AzureDpsFramework;

public sealed class RequiredCertificateAuthenticationProvider : AuthenticationProvider
{
    private readonly AuthenticationProvider inner;
    private readonly Action<InvalidOperationException>? onMissingCertificate;

    public RequiredCertificateAuthenticationProvider(AuthenticationProvider inner, Action<InvalidOperationException>? onMissingCertificate = null)
        : base(inner.RemoteCertificateValidationCallback, inner.LocalCertificateSelectionCallback)
    {
        this.inner = inner;
        this.onMissingCertificate = onMissingCertificate;
    }

    public override string GetRegistrationId() => inner.GetRegistrationId();
    public override Task<AuthenticationCredentials> GetPrimaryBootAuthenticationCredentialsAsync(CancellationToken cancellationToken = default) =>
        inner.GetPrimaryBootAuthenticationCredentialsAsync(cancellationToken);
    public override Task<AuthenticationCredentials?> GetSecondaryBootAuthenticationCredentialsAsync(CancellationToken cancellationToken = default) =>
        inner.GetSecondaryBootAuthenticationCredentialsAsync(cancellationToken);

    public override Task<AuthenticationCredentials> GetPrimaryOperationalAuthenticationCredentialsAsync(
        string hostName, string deviceId, IReadOnlyList<string>? issuedClientCertificates, CancellationToken cancellationToken = default)
    {
        RequireIssuedCertificate(issuedClientCertificates);
        return inner.GetPrimaryOperationalAuthenticationCredentialsAsync(hostName, deviceId, issuedClientCertificates, cancellationToken);
    }

    public override Task<AuthenticationCredentials?> GetSecondaryOperationalAuthenticationCredentialsAsync(
        string hostName, string deviceId, IReadOnlyList<string>? issuedClientCertificates, CancellationToken cancellationToken = default)
    {
        RequireIssuedCertificate(issuedClientCertificates);
        return inner.GetSecondaryOperationalAuthenticationCredentialsAsync(hostName, deviceId, issuedClientCertificates, cancellationToken);
    }

    private void RequireIssuedCertificate(IReadOnlyList<string>? chain)
    {
        if (chain == null || chain.Count == 0)
        {
            var error = new InvalidOperationException("The selected profile requires an issued operational certificate, but DPS did not return one. Check the enrollment's ADR CA and certificate policy.");
            onMissingCertificate?.Invoke(error);
            throw error;
        }
    }
}
