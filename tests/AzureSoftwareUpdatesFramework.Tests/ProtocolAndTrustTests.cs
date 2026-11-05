using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzureSoftwareUpdatesFramework;
using Xunit;

namespace AzureSoftwareUpdatesFramework.Tests;

public sealed class ProtocolAndTrustTests
{
    [Fact]
    public void GetUpdateRequestUsesPublishedRegularRouteFields()
    {
        var properties = new AgentInformation(
            "test-agent",
            "skittlesorter/application:1",
            new Dictionary<string, string> { ["manufacturer"] = "SkittleSorter", ["model"] = "Pi4" });
        var body = SoftwareUpdatesProtocol.GetUpdateRequest(
            properties, new UpdateId("SkittleSorter", "Application", "1.0"),
            "agent-etag", "service-etag");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("agent-etag", root.GetProperty("agentInfoEtag").GetString());
        Assert.Equal("service-etag", root.GetProperty("serviceConfigEtag").GetString());
        Assert.Equal("1.0", root.GetProperty("installedUpdateId").GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("agentInfo", out _));
        Assert.Equal(
            "$dps/registrations/POST/iotdps-get-deviceupdate/?$rid=abc%201",
            SoftwareUpdatesProtocol.RequestTopic(SoftwareUpdatesProtocol.GetUpdateOperation, "abc 1"));
    }

    [Fact]
    public void UpdateRequestWithoutEtagsIncludesAgentInfoAndOmitsUnknownInstalledId()
    {
        var properties = new AgentInformation(
            "test-agent", "profile-1", new Dictionary<string, string> { ["model"] = "Pi4" });
        using var document = JsonDocument.Parse(
            SoftwareUpdatesProtocol.GetUpdateRequest(properties, null, null, null));
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("agentInfo", out _));
        Assert.False(root.TryGetProperty("installedUpdateId", out _));
    }

    [Fact]
    public void DpsResponseTopicCarriesStatusRetryAfterAndRequestCorrelation()
    {
        Assert.True(DpsResponseTopicParser.TryParse(
            "$dps/registrations/res/503/?$rid=42&retry-after=30", out var response));
        Assert.Equal(503, response!.StatusCode);
        Assert.Equal("42", response.RequestId);
        Assert.Equal(TimeSpan.FromSeconds(30), response.RetryAfter);
        Assert.False(DpsResponseTopicParser.TryParse(
            "$dps/registrations/res/200/?$rid=42&$rid=43", out _));
        Assert.False(DpsResponseTopicParser.TryParse(
            "$dps/registrations/res/200/?retry-after=3", out _));
    }

    [Fact]
    public void InstallerCommandUsesFixedNonInteractiveHelperAndOnlyIncomingBasename()
    {
        var incoming = Path.GetFullPath(SoftwareUpdatesConfiguration.FixedIncomingDirectory);
        var payloadPath = Path.Combine(incoming, "2.0.0-abc.tar.gz");
        var command = InstallerHelperCommand.Create(payloadPath, "2.0.0", "linux-arm64");

        Assert.Equal("/usr/bin/sudo", command.FileName);
        Assert.Equal(
            new[]
            {
                "-n", "/usr/lib/skittlesorter/installer-helper", "install",
                "--payload", "2.0.0-abc.tar.gz", "--version", "2.0.0", "--runtime", "linux-arm64"
            },
            command.ArgumentList);
        Assert.Throws<InvalidDataException>(() =>
            InstallerHelperCommand.Create(Path.Combine(incoming, "..", "escape.tar.gz"), "2.0.0", "linux-arm64"));
        Assert.Throws<InvalidDataException>(() =>
            InstallerHelperCommand.Create(payloadPath, "-dangerous", "linux-arm64"));
        Assert.Equal(256L * 1024 * 1024, SoftwareUpdatesConfiguration.InstallerHelperMaximumPayloadBytes);
        Assert.Equal(
            Path.GetFullPath("/var/cache/skittlesorter-updates/incoming"), incoming);
    }

    [Fact]
    public void NestedRs256ChainAndManifestDigestVerifyAgainstConfiguredRoot()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var offer = SignedOffer(root, signing, Manifest());

        var result = SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64");

        Assert.Equal(new UpdateId("SkittleSorter", "Application", "2.0.0"), result.UpdateId);
        Assert.Equal("payload.tar.gz", result.FileName);
        Assert.Equal(4, result.FileSize);
        Assert.Equal(new Uri("https://updates.example.test/payload.tar.gz"), result.DownloadUri);
    }

    [Fact]
    public void GeneratedImportManifestRequiresDeviceSchemaAndStepNormalizationBeyondFileMap()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "adr-1.1.0.importmanifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject();
        var file = manifest["files"]![0]!.DeepClone().AsObject();
        var fileId = file["filename"]!.GetValue<string>();
        file["fileName"] = file["filename"]!.DeepClone();
        file.Remove("filename");
        manifest["files"] = new JsonObject { [fileId] = file };
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        OfferedUpdate Sign() => SignedOffer(root, signing, manifest.ToJsonString()) with
        {
            FileUrls = new Dictionary<string, string>
            {
                [fileId] = "https://updates.example.test/" + fileId
            }
        };
        var roots = new[] { Root("root-1", root) };

        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(Sign(), roots, "linux-arm64"));
        manifest["manifestVersion"] = "5";
        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(Sign(), roots, "linux-arm64"));
        var step = manifest["instructions"]!["steps"]![0]!.AsObject();
        step.Remove("type");
        step.Remove("description");
        var accepted = SignedManifestVerifier.Verify(Sign(), roots, "linux-arm64");

        Assert.Equal("1.1.0", accepted.UpdateId.Version);
        Assert.Equal(fileId, accepted.FileId);
        Assert.Equal(37963510, accepted.FileSize);
    }

    [Fact]
    public void TamperedManifestIsRejectedBeforeItsHandlerOrDownloadIsUsed()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var offer = SignedOffer(root, signing, Manifest());
        offer = offer with { UpdateManifest = offer.UpdateManifest.Replace("2.0.0", "9.0.0", StringComparison.Ordinal) };

        Assert.Throws<CryptographicException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));
    }

    [Fact]
    public void UnknownOrDisabledRootDoesNotBecomeTrustedFromOffer()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var offer = SignedOffer(root, signing, Manifest());

        Assert.Throws<CryptographicException>(() =>
            SignedManifestVerifier.Verify(offer, Array.Empty<TrustedRootKey>(), "linux-arm64"));
        Assert.Throws<CryptographicException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) with { Disabled = true } }, "linux-arm64"));
    }

    [Fact]
    public void NonRs256ManifestHeaderIsRejectedEvenWhenItsSignatureIsValid()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var offer = SignedOffer(root, signing, Manifest());
        var parts = offer.UpdateManifestSignature.Split('.');
        var payload = Encoding.UTF8.GetString(FromBase64Url(parts[1]));
        var header = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        using var headerDocument = JsonDocument.Parse(header);
        var sjwk = headerDocument.RootElement.GetProperty("sjwk").GetString()!;
        offer = offer with
        {
            UpdateManifestSignature = SignCompact(
                signing, JsonSerializer.Serialize(new { alg = "PS256", sjwk }), payload)
        };

        Assert.Throws<CryptographicException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));
    }

    [Fact]
    public void UnknownHandlerAndWrongRuntimeAreRejectedAfterSignatureVerification()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var wrongHandler = Manifest(handler: "vendor/unknown:1");
        var offer = SignedOffer(root, signing, wrongHandler);
        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));

        offer = SignedOffer(root, signing, Manifest());
        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm"));

        offer = SignedOffer(root, signing, ManifestWithInstalledCriteria("different"));
        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));
    }

    [Fact]
    public void MultistepOrNonHttpsUpdateCannotReachInstallerManifest()
    {
        using var root = RSA.Create(2048);
        using var signing = RSA.Create(2048);
        var multiStepManifest = Manifest(stepCount: 2);
        var offer = SignedOffer(root, signing, multiStepManifest);
        Assert.Throws<NotSupportedException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));

        offer = SignedOffer(root, signing, Manifest()) with
        {
            FileUrls = new Dictionary<string, string> { ["f0"] = "http://updates.example.test/payload.tar.gz" }
        };
        Assert.Throws<InvalidDataException>(() =>
            SignedManifestVerifier.Verify(offer, new[] { Root("root-1", root) }, "linux-arm64"));
    }

    [Fact]
    public async Task StateStoreAtomicallyRestoresInstalledIdAndOwedReport()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "state-tests", Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var store = new UpdateStateStore(path);
            store.State.InstalledUpdateId = new UpdateId("SkittleSorter", "Application", "1.0.0");
            store.State.OwedReport = new InstallReport(
                "workflow-1", store.State.InstalledUpdateId,
                new InstallResult("FAILED", "AGENT_CORE", -5, "50000000", "installer error", null));
            store.State.FailedWorkflowId = "workflow-1";
            store.State.ConsecutiveFailures = 1;
            store.State.RetryAfterUtc = DateTimeOffset.UtcNow.AddHours(1);
            await store.SaveAsync();

            var restored = new UpdateStateStore(path);
            Assert.Equal("1.0.0", restored.State.InstalledUpdateId!.Version);
            Assert.Equal("workflow-1", restored.State.OwedReport!.WorkflowId);
            Assert.Equal("workflow-1", restored.State.FailedWorkflowId);
            Assert.Equal(1, restored.State.ConsecutiveFailures);
            Assert.NotNull(restored.State.RetryAfterUtc);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PayloadMustMatchSignedSizeAndSha256BeforeItIsReturned()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "payload-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = new byte[] { 1, 2, 3, 4 };
            var manifest = new VerifiedApplicationManifest(
                new UpdateId("SkittleSorter", "Application", "2.0.0"),
                "payload.tar.gz",
                "payload.tar.gz",
                bytes.Length,
                SHA256.HashData(bytes),
                new Uri("https://updates.example.test/payload.tar.gz"));
            using (var downloader = new VerifiedPayloadDownloader(new FixedResponseHandler(bytes)))
            {
                var path = await downloader.DownloadAsync(manifest, directory, 64, CancellationToken.None);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
                File.Delete(path);
            }

            using var invalidDownloader = new VerifiedPayloadDownloader(new FixedResponseHandler(new byte[] { 4, 3, 2, 1 }));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                invalidDownloader.DownloadAsync(manifest, directory, 64, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(directory));

            var insecure = manifest with { DownloadUri = new Uri("http://updates.example.test/payload.tar.gz") };
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                invalidDownloader.DownloadAsync(insecure, directory, 64, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static string Manifest(
        int stepCount = 1,
        string handler = SoftwareUpdatesProtocol.SupportedHandler,
        string runtime = "linux-arm64")
        => ManifestWithInstalledCriteria("2.0.0", stepCount, handler, runtime);

    private static string ManifestWithInstalledCriteria(
        string installedCriteria,
        int stepCount = 1,
        string handler = SoftwareUpdatesProtocol.SupportedHandler,
        string runtime = "linux-arm64")
    {
        var payloadHash = Convert.ToBase64String(SHA256.HashData(new byte[] { 1, 2, 3, 4 }));
        var step = new
        {
            handler,
            files = new[] { "f0" },
            handlerProperties = new { installedCriteria, runtime }
        };
        var manifest = new
        {
            manifestVersion = "5",
            updateId = new { provider = "SkittleSorter", name = "Application", version = "2.0.0" },
            instructions = new { steps = Enumerable.Repeat(step, stepCount).ToArray() },
            files = new Dictionary<string, object>
            {
                ["f0"] = new
                {
                    fileName = "payload.tar.gz",
                    sizeInBytes = 4,
                    hashes = new { sha256 = payloadHash }
                }
            }
        };
        return JsonSerializer.Serialize(manifest);
    }

    private static OfferedUpdate SignedOffer(RSA root, RSA signing, string manifest)
    {
        var publicParameters = signing.ExportParameters(false);
        var jwk = JsonSerializer.Serialize(new
        {
            kty = "RSA",
            n = Base64Url(publicParameters.Modulus!),
            e = Base64Url(publicParameters.Exponent!)
        });
        var sjwk = SignCompact(
            root,
            JsonSerializer.Serialize(new { alg = "RS256", kid = "root-1" }),
            jwk);
        var manifestHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
        var signature = SignCompact(
            signing,
            JsonSerializer.Serialize(new { alg = "RS256", sjwk }),
            JsonSerializer.Serialize(new { sha256 = manifestHash }));
        return new OfferedUpdate(
            "workflow-1",
            manifest,
            signature,
            new Dictionary<string, string> { ["f0"] = "https://updates.example.test/payload.tar.gz" });
    }

    private static string SignCompact(RSA key, string header, string payload)
    {
        var encodedHeader = Base64Url(Encoding.UTF8.GetBytes(header));
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var input = encodedHeader + "." + encodedPayload;
        var signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + Base64Url(signature);
    }

    private static TrustedRootKey Root(string kid, RSA key)
    {
        var parameters = key.ExportParameters(false);
        return new TrustedRootKey(kid, Base64Url(parameters.Modulus!), Base64Url(parameters.Exponent!));
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '='));
    }

    private sealed class FixedResponseHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
    }
}
