import json
import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "test-unified.ps1"


class HubIdentityCheckTests(unittest.TestCase):
    def run_check(self, scenario):
        command = r"""
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($env:TEST_SCRIPT, [ref]$tokens, [ref]$errors)
$definition = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-HubDevice'
}, $true)
. ([scriptblock]::Create($definition.Extent.Text))
$ResourceGroupName = 'demo'
$HubName = 'demo'
$registration = 'skittlesorter-002'
$SubscriptionId = 'subscription'
function az {
    if ($env:TEST_SCENARIO -eq 'found') {
        $global:LASTEXITCODE = 0
        '"skittlesorter-002"'
    } elseif ($env:TEST_SCENARIO -eq 'absent') {
        $global:LASTEXITCODE = 3
        'ERROR: ErrorCode:DeviceNotFound;404'
    } else {
        $global:LASTEXITCODE = 1
        'ERROR: IotHubUnauthorized'
    }
}
try {
    $found = Test-HubDevice
    [pscustomobject]@{found = $found; error = $null} | ConvertTo-Json -Compress
} catch {
    [pscustomobject]@{found = $null; error = $_.Exception.Message} | ConvertTo-Json -Compress
}
"""
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-Command", command],
            cwd=ROOT, capture_output=True, text=True, check=True,
            env={**os.environ, "TEST_SCRIPT": str(SCRIPT), "TEST_SCENARIO": scenario},
        )
        return json.loads(result.stdout.strip().splitlines()[-1])

    def test_exact_identity_and_cascaded_deletion(self):
        self.assertTrue(self.run_check("found")["found"])
        self.assertFalse(self.run_check("absent")["found"])

    def test_authorization_failure_is_not_treated_as_missing(self):
        result = self.run_check("unauthorized")
        self.assertIn("IotHubUnauthorized", result["error"])


if __name__ == "__main__":
    unittest.main()
