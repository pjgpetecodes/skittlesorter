using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AzureSoftwareUpdatesFramework;

public sealed record VerifiedApplicationManifest(
    UpdateId UpdateId,
    string FileId,
    string FileName,
    long FileSize,
    byte[] FileSha256,
    Uri DownloadUri);

public static class SignedManifestVerifier
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumTokenBytes = 64 * 1024;

    public static VerifiedApplicationManifest Verify(
        OfferedUpdate offer,
        IReadOnlyList<TrustedRootKey> independentlyTrustedRoots,
        string expectedRuntime)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (string.IsNullOrWhiteSpace(offer.WorkflowId) ||
            Encoding.UTF8.GetByteCount(offer.UpdateManifest) is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("Workflow id or manifest size is invalid.");

        var (manifestHeader, manifestPayload, manifestSignature, manifestSignedBytes) =
            DecodeJws(offer.UpdateManifestSignature);
        RequireRs256(manifestHeader);
        var embeddedSjwk = RequiredString(manifestHeader, "sjwk");

        var (sjwkHeader, sjwkPayload, sjwkSignature, sjwkSignedBytes) = DecodeJws(embeddedSjwk);
        RequireRs256(sjwkHeader);
        var kid = RequiredString(sjwkHeader, "kid");
        var trustedRoot = independentlyTrustedRoots.SingleOrDefault(root => root.Kid == kid);
        if (trustedRoot is null || trustedRoot.Disabled)
            throw new CryptographicException($"Signing key references unknown or disabled trusted root '{kid}'.");

        using var rootRsa = ImportKey(trustedRoot.Modulus, trustedRoot.Exponent);
        if (!rootRsa.VerifyData(
                sjwkSignedBytes, sjwkSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Nested signing-key JWS did not verify against the configured root.");

        using var sjwkPayloadDocument = JsonDocument.Parse(sjwkPayload);
        var signingKey = sjwkPayloadDocument.RootElement;
        using var signingRsa = ImportKey(
            RequiredString(signingKey, "n"), RequiredString(signingKey, "e"));
        if (!signingRsa.VerifyData(
                manifestSignedBytes, manifestSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Manifest JWS did not verify against its signed key.");

        using var manifestPayloadDocument = JsonDocument.Parse(manifestPayload);
        var expectedManifestHash = DecodeBase64(RequiredString(manifestPayloadDocument.RootElement, "sha256"));
        var actualManifestHash = SHA256.HashData(Encoding.UTF8.GetBytes(offer.UpdateManifest));
        if (expectedManifestHash.Length != actualManifestHash.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedManifestHash, actualManifestHash))
            throw new CryptographicException("Signed manifest digest does not match the offered manifest.");

        // Do not inspect even the update handler or download URL until the complete signing chain passed.
        return ParseSupportedManifest(offer, Encoding.UTF8.GetBytes(offer.UpdateManifest), expectedRuntime);
    }

    private static VerifiedApplicationManifest ParseSupportedManifest(
        OfferedUpdate offer, byte[] manifestBytes, string expectedRuntime)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        var root = document.RootElement;
        if (RequiredString(root, "manifestVersion") != "5")
            throw new NotSupportedException("Only regular update manifest version 5 is supported.");

        var updateIdElement = RequiredObject(root, "updateId");
        var id = new UpdateId(
            RequiredString(updateIdElement, "provider"),
            RequiredString(updateIdElement, "name"),
            RequiredString(updateIdElement, "version"));

        var instructions = RequiredObject(root, "instructions");
        EnsureOnlyProperties(instructions, "steps");
        var steps = RequiredArray(instructions, "steps");
        if (steps.GetArrayLength() != 1)
            throw new NotSupportedException("Only a single-step regular update is supported.");
        var step = steps[0];
        EnsureOnlyProperties(step, "handler", "files", "handlerProperties");
        if (RequiredString(step, "handler") != SoftwareUpdatesProtocol.SupportedHandler)
            throw new NotSupportedException("The update handler is not the supported Skittle Sorter application handler.");
        var handlerProperties = RequiredObject(step, "handlerProperties");
        EnsureOnlyProperties(handlerProperties, "installedCriteria", "runtime");
        if (RequiredString(handlerProperties, "installedCriteria") != id.Version ||
            RequiredString(handlerProperties, "runtime") != expectedRuntime)
            throw new NotSupportedException(
                "The handler's installedCriteria must match updateId.version and runtime must match the configured Pi runtime.");

        var files = RequiredArray(step, "files");
        if (files.GetArrayLength() != 1 || files[0].ValueKind != JsonValueKind.String)
            throw new NotSupportedException("The supported handler requires exactly one inline payload file.");
        var fileId = files[0].GetString()!;
        if (offer.FileUrls.Count != 1 ||
            !offer.FileUrls.TryGetValue(fileId, out var rawUrl) ||
            !Uri.TryCreate(rawUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(downloadUri.UserInfo) ||
            !string.IsNullOrEmpty(downloadUri.Fragment))
            throw new InvalidDataException("The payload URL is missing, ambiguous, or not HTTPS.");

        var fileMap = RequiredObject(root, "files");
        if (fileMap.EnumerateObject().Count() != 1)
            throw new NotSupportedException("The supported handler requires exactly one declared payload file.");
        var file = RequiredObject(fileMap, fileId);
        EnsureOnlyProperties(file, "fileName", "sizeInBytes", "hashes");
        var size = RequiredInt64(file, "sizeInBytes");
        var fileName = RequiredString(file, "fileName");
        if (size <= 0 || fileName.Length > 128 ||
            !fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(fileName) != fileName ||
            fileName.Contains('/') || fileName.Contains('\\'))
            throw new InvalidDataException("Payload must have a safe .tar.gz filename and positive declared size.");

        var hashes = RequiredObject(file, "hashes");
        EnsureOnlyProperties(hashes, "sha256");
        var hash = DecodeBase64(RequiredString(hashes, "sha256"));
        if (hash.Length != 32)
            throw new InvalidDataException("The payload SHA-256 must be exactly 32 bytes.");

        return new VerifiedApplicationManifest(id, fileId, fileName, size, hash, downloadUri);
    }

    private static (JsonElement Header, byte[] Payload, byte[] Signature, byte[] SignedBytes) DecodeJws(string compact)
    {
        if (Encoding.UTF8.GetByteCount(compact) > MaximumTokenBytes)
            throw new InvalidDataException("JWS exceeds the configured size limit.");
        var parts = compact.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty))
            throw new InvalidDataException("A compact JWS must contain exactly three non-empty parts.");
        var headerBytes = DecodeBase64Url(parts[0]);
        var payloadBytes = DecodeBase64Url(parts[1]);
        var signature = DecodeBase64Url(parts[2]);
        using var headerDocument = JsonDocument.Parse(headerBytes);
        return (
            headerDocument.RootElement.Clone(),
            payloadBytes,
            signature,
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]));
    }

    private static void RequireRs256(JsonElement header)
    {
        if (RequiredString(header, "alg") != "RS256")
            throw new CryptographicException("Only RS256 signatures are accepted.");
    }

    private static RSA ImportKey(string modulus, string exponent)
    {
        var n = DecodeBase64(modulus);
        var e = DecodeBase64(exponent);
        if (n.Length < 256 || n.Length > 1024 || e.Length is < 1 or > 8)
            throw new CryptographicException("RSA key size or exponent is outside the accepted bounds.");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportParameters(new RSAParameters { Modulus = n, Exponent = e });
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static byte[] DecodeBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static byte[] DecodeBase64Url(string value) => DecodeBase64(value);

    private static string RequiredString(JsonElement parent, string name)
    {
        var value = RequiredProperty(parent, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"'{name}' must be a non-empty string.");
        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement parent, string name)
    {
        var value = RequiredProperty(parent, name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new InvalidDataException($"'{name}' must be an integer.");
        return number;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        var value = RequiredProperty(parent, name);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"'{name}' must be an object.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        var value = RequiredProperty(parent, name);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"'{name}' must be an array.");
        return value;
    }

    private static JsonElement RequiredProperty(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Expected an object while reading '{name}'.");
        JsonElement found = default;
        var count = 0;
        foreach (var property in parent.EnumerateObject())
        {
            if (property.NameEquals(name))
            {
                found = property.Value;
                count++;
            }
        }
        if (count != 1)
            throw new InvalidDataException($"Required field '{name}' is missing or duplicated.");
        return found;
    }

    private static void EnsureOnlyProperties(JsonElement parent, params string[] allowed)
    {
        var accepted = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in parent.EnumerateObject())
        {
            if (!accepted.Contains(property.Name))
                throw new NotSupportedException($"Unsupported manifest property '{property.Name}'.");
        }
    }
}
