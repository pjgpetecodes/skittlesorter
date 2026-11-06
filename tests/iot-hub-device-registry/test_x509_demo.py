import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "setup-x509-demo.ps1"


class X509DemoTests(unittest.TestCase):
    def test_creates_verified_ca_and_separate_x509_profile_without_overwriting(self):
        command = r"""
$ErrorActionPreference = 'Stop'
$directory = $env:TEST_DIRECTORY
$rsa = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=test-issuer', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $false, 0, $true))
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
    [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
$issuer = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(400))
[IO.File]::WriteAllText("$directory\issuer.pem", $issuer.ExportCertificatePem())
[IO.File]::WriteAllText("$directory\issuer.key", $rsa.ExportPkcs8PrivateKeyPem())
[IO.File]::WriteAllText("$directory\chain.pem", $issuer.ExportCertificatePem())
$issuer.Dispose()
$rsa.Dispose()
[IO.File]::WriteAllText("$directory\source.json",
    '{"IoTHub":{"DpsProvisioning":{"IdScope":"test-scope","RegistrationId":"demo-device","AttestationMethod":"SymmetricKey","DeviceKeyBase64":"private","EnrollmentGroupKeyBase64":"private","AttestationCertPath":"","AttestationKeyPath":"","AttestationCertChainPath":"","RequestOperationalCertificate":true,"AutoGenerateCsr":true}}}')
$global:calls = @()
function global:az {
    $global:calls += ,@($args)
    $global:LASTEXITCODE = 0
    if ($args[0..2] -join ' ' -eq 'iot dps show') { '"test-scope"' }
    elseif ($args[0..3] -join ' ' -eq 'iot dps enrollment-group list') { '[]' }
    elseif ($args[0..3] -join ' ' -eq 'iot dps certificate list') { '{"value":[]}' }
    else { '{}' }
}
$params = @{
    SubscriptionId = 'subscription'; ResourceGroupName = 'group'; DpsName = 'dps'
    NamespaceName = 'namespace'; EnrollmentId = 'x509-group'
    IssuerCertificatePath = "$directory\issuer.pem"; IssuerKeyPath = "$directory\issuer.key"
    IssuerChainPath = "$directory\chain.pem"; SourceConfigPath = "$directory\source.json"
    OutputConfigPath = "$directory\output.json"; DeviceDirectory = "$directory\device"
}
& $env:TEST_SCRIPT @params -Confirm:$false
$config = Get-Content -Raw "$directory\output.json" | ConvertFrom-Json
$leaf = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile(
    $config.IoTHub.DpsProvisioning.AttestationCertPath, $config.IoTHub.DpsProvisioning.AttestationKeyPath)
try {
    $subject = $leaf.Subject
    $privateKey = $leaf.HasPrivateKey
} finally { $leaf.Dispose() }
try { & $env:TEST_SCRIPT @params -Confirm:$false; $refused = $false } catch { $refused = $true }
[pscustomobject]@{
    calls = $global:calls; config = $config.IoTHub.DpsProvisioning
    subject = $subject; privateKey = $privateKey; refused = $refused
} | ConvertTo-Json -Depth 10 -Compress
"""
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(
                ["pwsh", "-NoProfile", "-Command", command],
                cwd=ROOT, capture_output=True, text=True, check=True,
                env={**os.environ, "TEST_SCRIPT": str(SCRIPT), "TEST_DIRECTORY": directory},
            )
            data = json.loads(result.stdout.strip().splitlines()[-1])
        config = data["config"]
        self.assertEqual(config["AttestationMethod"], "X509")
        self.assertFalse(config["RequestOperationalCertificate"])
        self.assertEqual(config["DeviceKeyBase64"], "")
        self.assertEqual(config["EnrollmentGroupKeyBase64"], "")
        self.assertEqual(data["subject"], "CN=demo-device")
        self.assertTrue(data["privateKey"])
        self.assertTrue(data["refused"])
        calls = data["calls"]
        ca = next(call for call in calls if call[:4] == ["iot", "dps", "certificate", "create"])
        self.assertEqual(ca[ca.index("--verified") + 1], "true")
        group = next(call for call in calls if call[:4] == ["iot", "dps", "enrollment-group", "create"])
        self.assertIn("--certificate-path", group)
        self.assertEqual(group[group.index("--enrollment-id") + 1], "x509-group")


if __name__ == "__main__":
    unittest.main()
