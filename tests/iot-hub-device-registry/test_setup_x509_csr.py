import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "setup.ps1"


class X509SetupTests(unittest.TestCase):
    def test_existing_link_to_other_resource_is_rejected(self):
        command = r"""
$ErrorActionPreference = 'Stop'
function global:az {
    $global:LASTEXITCODE = 0
    $joined = $args -join ' '
    if ($joined -like 'account show*') { return 'subscription' }
    if ($joined -like 'group exists*') { return 'false' }
    if ($joined -like 'iot dps show*') { return 'dps-id' }
    if ($joined -like 'iot hub show*') {
        if ($args -contains 'properties.disableLocalAuth') { return 'false' }
        return 'hub-id'
    }
    if ($joined -like 'iot adr ns link dps list*') {
        if ($joined -like '*resourceId*') {
            if ($env:MISMATCH -eq 'dps') { return 'other-dps-id' }
            return 'dps-id'
        }
        return 'primary-dps'
    }
    if ($joined -like 'iot adr ns link hub list*') {
        if ($joined -like '*resourceId*') { return 'other-hub-id' }
        return 'primary-hub'
    }
}
try {
    & $env:TEST_SCRIPT -SubscriptionId subscription -ResourceGroupName group -Location uksouth `
        -NamespaceName namespace -HubName hub -DpsName dps -EnrollmentId x509-group `
        -AppConfigPath "$env:TEST_DIRECTORY\config.json" -Confirm:$false
    throw 'Expected a mismatched link to be refused'
} catch {
    if ($_.Exception.Message -notlike "*primary-$env:MISMATCH*not the requested*") { throw }
}
exit 0
"""
        for mismatch in ("dps", "hub"):
            with self.subTest(mismatch=mismatch), tempfile.TemporaryDirectory() as directory:
                subprocess.run(
                    ["pwsh", "-NoProfile", "-Command", command],
                    cwd=ROOT, capture_output=True, text=True, check=True,
                    env={**os.environ, "TEST_SCRIPT": str(SCRIPT),
                         "TEST_DIRECTORY": directory, "MISMATCH": mismatch},
                )

    def test_setup_creates_x509_enrollment_and_csr_profile(self):
        command = r"""
$ErrorActionPreference = 'Stop'
$global:calls = @()
function global:az {
    $global:calls += ,@($args)
    $global:LASTEXITCODE = 0
    $prefix = ($args | Select-Object -First 4) -join ' '
    if ($prefix -like 'account show*') { return 'subscription' }
    if ($prefix -like 'group exists*') { return 'false' }
    if ($prefix -like 'resource list*') { return }
    if ($prefix -like 'iot dps show*') {
        if ($args -contains 'properties.idScope') {
            if ($args -contains 'json') { return '"scope"' }
            return 'scope'
        }
        return 'dps-id'
    }
    if ($prefix -like 'iot hub show*') {
        if ($args -contains 'properties.disableLocalAuth') { return 'false' }
        return 'hub-id'
    }
    if (($args -join ' ') -like 'iot adr ns link dps list*resourceId*') { return 'dps-id' }
    if (($args -join ' ') -like 'iot adr ns link hub list*resourceId*') { return 'hub-id' }
    if ($prefix -like 'iot dps enrollment-group list*' -and $args -contains 'json') { return '[]' }
    if ($prefix -like 'iot dps certificate list*') { return '{"value":[]}' }
    if ($args -contains 'json') { return '{}' }
}
& $env:TEST_SCRIPT -SubscriptionId subscription -ResourceGroupName group -Location uksouth `
    -NamespaceName namespace -HubName hub -DpsName dps -EnrollmentId x509-group `
    -RegistrationId demo-device -AppConfigPath "$env:TEST_DIRECTORY\config.json" `
    -BootstrapDirectory "$env:TEST_DIRECTORY\bootstrap" -Confirm:$false
$config = (Get-Content -Raw "$env:TEST_DIRECTORY\config.json" | ConvertFrom-Json).IoTHub.DpsProvisioning
[pscustomobject]@{calls=$global:calls; config=$config} | ConvertTo-Json -Depth 10 -Compress
"""
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(
                ["pwsh", "-NoProfile", "-Command", command],
                cwd=ROOT, capture_output=True, text=True, check=True,
                env={**os.environ, "TEST_SCRIPT": str(SCRIPT), "TEST_DIRECTORY": directory},
            )
            data = json.loads(result.stdout.strip().splitlines()[-1])
            config = data["config"]
            self.assertTrue(Path(config["AttestationCertPath"]).is_file())
            self.assertTrue(Path(config["AttestationKeyPath"]).is_file())
        self.assertEqual(config["AttestationMethod"], "X509")
        self.assertTrue(config["RequestOperationalCertificate"])
        self.assertTrue(config["AutoGenerateCsr"])
        groups = [call for call in data["calls"] if call[:4] == ["iot", "dps", "enrollment-group", "create"]]
        self.assertEqual(len(groups), 1)
        self.assertIn("--certificate-path", groups[0])
        self.assertIn("--adr-cert-policy-name", groups[0])
        self.assertFalse(any("--show-keys" in call for call in data["calls"]))


if __name__ == "__main__":
    unittest.main()
