import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "setup.ps1"


class SharedAccessSetupTests(unittest.TestCase):
    def run_setup(self, existing, disabled, update_result="false", what_if=False):
        command = r"""
$global:calls = @()
$global:disabled = $env:TEST_DISABLED
function global:az {
    $global:calls += ,@($args)
    $global:LASTEXITCODE = 0
    $prefix = ($args | Select-Object -First 4) -join ' '
    if ($prefix -like 'account show*') { return 'subscription' }
    if ($prefix -like 'group exists*') { return 'false' }
    if ($prefix -like 'resource list*') {
        if ($env:TEST_EXISTING -eq 'true' -and ($args -join ' ') -match 'IotHubs') { return 'hub' }
        return
    }
    if ($prefix -like 'iot dps show*') { return 'dps-id' }
    if (($args -join ' ') -like 'iot adr ns link dps list*resourceId*') { return 'dps-id' }
    if ($prefix -like 'iot hub create*') { $global:disabled = $env:TEST_DISABLED; return }
    if ($prefix -like 'iot hub update*') { $global:disabled = $env:TEST_UPDATE_RESULT; return }
    if ($prefix -like 'iot hub show*') {
        if ($args -contains 'location') { return 'uksouth' }
        if ($args -contains 'properties.disableLocalAuth') { return $global:disabled }
        return 'hub-id'
    }
    if (($args -join ' ') -like 'iot adr ns link hub add*') {
        $global:LASTEXITCODE = 1
        return 'test stop'
    }
}
try {
    & $env:TEST_SCRIPT -SubscriptionId subscription -ResourceGroupName demo -Location uksouth `
        -NamespaceName namespace -HubName hub -DpsName dps -EnrollmentId enrollment `
        -AppConfigPath $env:TEST_CONFIG `
        -WhatIf:($env:TEST_WHAT_IF -eq 'true') -Confirm:$false
    $errorText = $null
} catch { $errorText = $_.Exception.Message }
[pscustomobject]@{ calls = $global:calls; error = $errorText } | ConvertTo-Json -Depth 10 -Compress
"""
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(
                ["pwsh", "-NoProfile", "-Command", command],
                cwd=ROOT, capture_output=True, text=True, check=True,
                env={
                    **os.environ,
                    "TEST_SCRIPT": str(SCRIPT),
                    "TEST_CONFIG": str(Path(directory) / "config.json"),
                    "TEST_EXISTING": str(existing).lower(),
                    "TEST_DISABLED": disabled,
                    "TEST_UPDATE_RESULT": update_result,
                    "TEST_WHAT_IF": str(what_if).lower(),
                },
            )
        return json.loads(result.stdout.strip().splitlines()[-1])

    def test_new_and_existing_hubs_enable_shared_access(self):
        new = self.run_setup(existing=False, disabled="false")
        create = next(call for call in new["calls"] if call[:3] == ["iot", "hub", "create"])
        self.assertEqual(create[create.index("--disable-local-auth") + 1], "false")
        self.assertIn("test stop", new["error"])

        existing = self.run_setup(existing=True, disabled="true")
        update = next(call for call in existing["calls"] if call[:3] == ["iot", "hub", "update"])
        self.assertEqual(update[update.index("--disable-local-auth") + 1], "false")
        self.assertIn("test stop", existing["error"])

        already_enabled = self.run_setup(existing=True, disabled="false")
        self.assertFalse(any(call[:3] == ["iot", "hub", "update"] for call in already_enabled["calls"]))
        self.assertIn("test stop", already_enabled["error"])

    def test_override_and_preview_stop_before_linking(self):
        denied = self.run_setup(existing=True, disabled="true", update_result="true")
        self.assertIn("could not be verified", denied["error"])
        self.assertFalse(any(call[:6] == ["iot", "adr", "ns", "link", "hub", "add"] for call in denied["calls"]))

        preview = self.run_setup(existing=True, disabled="true", what_if=True)
        self.assertIsNone(preview["error"])
        self.assertEqual(preview["calls"], [])


if __name__ == "__main__":
    unittest.main()
