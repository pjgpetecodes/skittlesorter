import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "iot-hub-device-registry" / "test-unified.ps1"
RESET_SCRIPT = ROOT / "scripts" / "clean-test-start.ps1"


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
$HubAuthType = 'login'
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

    def run_reset(self, auth_type=None, scenario="found"):
        command = r"""
$global:commands = [System.Collections.Generic.List[string]]::new()
$global:hubChecks = 0
function az {
    $command = $args -join ' '
    $global:commands.Add($command)
    $global:LASTEXITCODE = 0
    switch -Wildcard ($command) {
        'iot dps show *' { '"test-scope"' }
        'iot dps enrollment-group show *' { '{"id":"group","namespace":"namespace"}' }
        'iot dps enrollment-group registration list *' { '["sorter"]' }
        'iot dps enrollment-group registration show *' {
            '{"id":"sorter","device":"sorter","hub":"hub.device.azure-devices.net"}'
        }
        'iot hub show *' { '"hub"' }
        'iot adr ns device list *' { '["sorter"]' }
        'iot hub device-identity show *' {
            $global:hubChecks++
            if ($env:TEST_SCENARIO -eq 'unauthorized') {
                $global:LASTEXITCODE = 1
                'ERROR: IotHubUnauthorized'
            } elseif ($env:TEST_SCENARIO -eq 'cascade' -and $global:hubChecks -gt 1) {
                $global:LASTEXITCODE = 3
                'ERROR: ErrorCode:DeviceNotFound;404'
            } else { '"sorter"' }
        }
    }
}
$parameters = @{
    AppConfigPath = $env:TEST_CONFIG
    SubscriptionId = 'subscription'
    DpsName = 'dps'
    IotHubName = 'hub'
    ResourceGroup = 'resource-group'
    EnrollmentGroupName = 'group'
    DeviceId = 'sorter'
    AdrNamespace = 'namespace'
}
if ($env:TEST_AUTH) { $parameters.HubAuthType = $env:TEST_AUTH }
$errorMessage = $null
try { & $env:TEST_RESET_SCRIPT @parameters -Confirm:$false }
catch { $errorMessage = $_.Exception.Message }
[pscustomobject]@{commands = $global:commands.ToArray(); error = $errorMessage} |
    ConvertTo-Json -Compress
"""
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            issued = directory / "sorter-issued.pem"
            assignment = directory / "sorter-issued.pem.connection.json"
            issued.write_text("retained certificate")
            assignment.write_text("{}")
            config = directory / "appsettings.json"
            config.write_text(json.dumps({
                "IoTHub": {"DpsProvisioning": {
                    "IdScope": "test-scope",
                    "RegistrationId": "sorter",
                    "IssuedCertFilePath": str(issued),
                }},
            }))
            result = subprocess.run(
                ["pwsh", "-NoProfile", "-Command", command],
                cwd=ROOT, capture_output=True, text=True, check=True,
                env={
                    **os.environ,
                    "TEST_RESET_SCRIPT": str(RESET_SCRIPT),
                    "TEST_CONFIG": str(config),
                    "TEST_AUTH": auth_type or "",
                    "TEST_SCENARIO": scenario,
                },
            )
            output = json.loads(result.stdout.strip().splitlines()[-1])
            output["assignment_exists"] = assignment.exists()
            self.assertEqual("retained certificate", issued.read_text())
            return output

    def test_authentication_choice_reaches_hub_checks_and_deletion(self):
        for requested, expected in [(None, "login"), ("key", "key")]:
            with self.subTest(auth_type=requested):
                result = self.run_reset(requested)
                self.assertIsNone(result["error"])
                hub_commands = [
                    command for command in result["commands"]
                    if command.startswith("iot hub device-identity ")
                ]
                self.assertEqual(3, len(hub_commands))
                for command in hub_commands:
                    self.assertIn(f"--auth-type {expected}", command)
                    self.assertIn("--device-id sorter", command)
                adr_delete = next(
                    c for c in result["commands"]
                    if c.startswith("iot adr ns device delete ")
                )
                self.assertIn("--no-wait", adr_delete)
                adr_wait = next(
                    c for c in result["commands"]
                    if c.startswith("iot adr ns device wait ")
                )
                self.assertIn("--deleted --timeout 120", adr_wait)
                self.assertLess(
                    result["commands"].index(adr_wait),
                    result["commands"].index(hub_commands[-1]),
                )
                self.assertFalse(result["assignment_exists"])

    def test_failed_authentication_never_falls_back_or_deletes(self):
        for auth_type in ["login", "key"]:
            with self.subTest(auth_type=auth_type):
                result = self.run_reset(auth_type, "unauthorized")
                self.assertIn("IotHubUnauthorized", result["error"])
                self.assertFalse(any(" delete " in c for c in result["commands"]))
                self.assertTrue(result["assignment_exists"])
                hub_commands = [
                    c for c in result["commands"]
                    if c.startswith("iot hub device-identity ")
                ]
                self.assertEqual(1, len(hub_commands))
                self.assertIn(f"--auth-type {auth_type}", hub_commands[0])

    def test_key_mode_handles_adr_cascaded_hub_deletion(self):
        result = self.run_reset("key", "cascade")
        self.assertIsNone(result["error"])
        self.assertFalse(any(
            c.startswith("iot hub device-identity delete ") for c in result["commands"]
        ))
        self.assertTrue(any(
            c.startswith("iot dps enrollment-group registration delete ")
            for c in result["commands"]
        ))
        self.assertFalse(result["assignment_exists"])


if __name__ == "__main__":
    unittest.main()
