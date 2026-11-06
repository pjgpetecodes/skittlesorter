import json
import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class LabSharedAccessTests(unittest.TestCase):
    def run_lab(self, channel, disabled, apply=True):
        script = ROOT / "scripts" / "updates" / channel / "new-lab.ps1"
        command = r"""
$global:calls = @()
function global:az {
    $global:calls += ,@($args)
    $global:LASTEXITCODE = 0
    $prefix = ($args | Select-Object -First 4) -join ' '
    if ($prefix -like 'extension show*') { return '1.0.0b2' }
    if ($prefix -like 'group exists*') { return 'false' }
    if ($prefix -like 'iot hub show*' -and $args -contains 'properties.disableLocalAuth') {
        return $env:TEST_DISABLED
    }
    if ($prefix -like 'iot hub show*' -or $prefix -like 'iot dps show*') { return 'resource-id' }
    if ($prefix -like 'iot adr ns link*' -and $args -contains 'hub') {
        $global:LASTEXITCODE = 1
        return 'test stop'
    }
    if ($prefix -like 'iot dps create*' -and $env:TEST_CHANNEL -eq 'device-update') {
        $global:LASTEXITCODE = 1
        return 'test stop'
    }
}
$params = @{
    Subscription='subscription'; ResourceGroup='demo'; Location='uksouth'; Hub='hub'
    Dps='dps'; Storage='storage123'; OperatorObjectId='operator'
    Apply=($env:TEST_APPLY -eq 'true')
}
if ($env:TEST_CHANNEL -eq 'adr') {
    $params.Namespace='namespace'; $params.UpdateInstance='update'
} else {
    $params.Account='account'; $params.Instance='instance'
}
try { & $env:TEST_SCRIPT @params; $errorText = $null }
catch { $errorText = $_.Exception.Message }
[pscustomobject]@{ calls = $global:calls; error = $errorText } | ConvertTo-Json -Depth 10 -Compress
"""
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-Command", command],
            cwd=ROOT, capture_output=True, text=True, check=True,
            env={
                **os.environ,
                "TEST_SCRIPT": str(script),
                "TEST_CHANNEL": channel,
                "TEST_DISABLED": disabled,
                "TEST_APPLY": str(apply).lower(),
            },
        )
        return json.loads(result.stdout.strip().splitlines()[-1])

    def test_create_and_verify_shared_access_before_linking(self):
        for channel in ("adr", "device-update"):
            with self.subTest(channel=channel):
                result = self.run_lab(channel, "false")
                create = next(call for call in result["calls"] if call[:3] == ["iot", "hub", "create"])
                self.assertEqual(create[create.index("--disable-local-auth") + 1], "false")
                self.assertIn("Azure command failed", result["error"])
                denied = self.run_lab(channel, "true")
                self.assertIn("not verified as enabled", denied["error"])
                self.assertFalse(any(call[:6] == ["iot", "adr", "ns", "link", "hub", "add"] for call in denied["calls"]))
                preview = self.run_lab(channel, "true", apply=False)
                self.assertIsNone(preview["error"])
                self.assertEqual(preview["calls"], [])


if __name__ == "__main__":
    unittest.main()
