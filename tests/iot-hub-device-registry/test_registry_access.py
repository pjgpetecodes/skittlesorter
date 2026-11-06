import json
import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "grant-registry-access.ps1"
OBJECT_ID = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
SCOPE = "/subscriptions/test/resourceGroups/demo/providers/Microsoft.Devices/IotHubs/demo"


class RegistryAccessTests(unittest.TestCase):
    def run_helper(self, existing="", what_if=False, fail_list=False):
        command = r"""
$Script = $env:TEST_SCRIPT
$Scope = $env:TEST_SCOPE
$ObjectId = $env:TEST_OBJECT_ID
$Existing = $env:TEST_EXISTING
$WhatIfMode = $env:TEST_WHAT_IF
$FailList = $env:TEST_FAIL_LIST
$global:calls = @()
function global:az {
    $global:calls += ,@($args)
    if ($args[2] -eq 'list') {
        $global:LASTEXITCODE = if ($FailList -eq 'true') { 1 } else { 0 }
        if ($Existing) { Write-Output $Existing }
    } else {
        $global:LASTEXITCODE = 0
    }
}
try {
    & $Script -HubResourceId $Scope -OperatorObjectId $ObjectId -PrincipalType User `
        -WhatIf:($WhatIfMode -eq 'true') -Confirm:$false
    $errorText = $null
} catch {
    $errorText = $_.Exception.Message
}
[pscustomobject]@{calls = $global:calls; error = $errorText} | ConvertTo-Json -Depth 10 -Compress
"""
        result = subprocess.run(
            ["pwsh", "-NoProfile", "-Command", command],
            cwd=ROOT, capture_output=True, text=True, check=True, env={
                **os.environ,
                "TEST_SCRIPT": str(SCRIPT),
                "TEST_SCOPE": SCOPE,
                "TEST_OBJECT_ID": OBJECT_ID,
                "TEST_EXISTING": existing,
                "TEST_WHAT_IF": str(what_if).lower(),
                "TEST_FAIL_LIST": str(fail_list).lower(),
            },
        )
        return json.loads(result.stdout.strip().splitlines()[-1])

    def test_creates_scoped_role_and_skips_existing_assignment(self):
        created = self.run_helper()
        self.assertIsNone(created["error"])
        self.assertEqual(len(created["calls"]), 2)
        call = created["calls"][1]
        self.assertEqual(call[:3], ["role", "assignment", "create"])
        self.assertEqual(call[call.index("--scope") + 1], SCOPE)
        self.assertEqual(call[call.index("--assignee-object-id") + 1], OBJECT_ID)
        self.assertEqual(call[call.index("--role") + 1], "IoT Hub Registry Contributor")
        existing = self.run_helper(existing="existing-assignment")
        self.assertIsNone(existing["error"])
        self.assertEqual(len(existing["calls"]), 1)

    def test_what_if_and_failed_lookup_do_not_grant_role(self):
        preview = self.run_helper(what_if=True)
        self.assertIsNone(preview["error"])
        self.assertEqual(len(preview["calls"]), 1)
        failed = self.run_helper(fail_list=True)
        self.assertIn("Unable to inspect", failed["error"])
        self.assertEqual(len(failed["calls"]), 1)


if __name__ == "__main__":
    unittest.main()
