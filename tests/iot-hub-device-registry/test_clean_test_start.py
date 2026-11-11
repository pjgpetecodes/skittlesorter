import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from scripts import clean_test_start


class CleanTestStartTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.config = self.directory / "appsettings.json"
        self.issued = self.directory / "sorter-issued.pem"
        self.cache = self.directory / "sorter-issued.pem.connection.json"
        self.key = self.directory / "sorter.key"
        self.csr = self.directory / "sorter.csr"
        for path in (self.issued, self.key, self.csr):
            path.write_text("retained")
        self.cache.write_text("{}")
        self.configuration = {
            "IoTHub": {"DpsProvisioning": {
                "RegistrationId": "sorter",
                "IdScope": "scope",
                "IssuedCertFilePath": str(self.issued),
            }},
            "Adr": {
                "SubscriptionId": "subscription",
                "ResourceGroupName": "resource-group",
                "NamespaceName": "namespace",
            },
        }
        self.config.write_text(json.dumps(self.configuration))
        self.calls = []
        self.hub_checks = 0
        self.cascade = False
        self.fail_operation = None
        self.timeout_operation = None
        self.scope = "scope"
        self.enrollment_namespace = "namespace"
        self.assigned_device = "sorter"
        self.assigned_hub = "hub.device.azure-devices.net"

    def azure(self, command, **kwargs):
        self.assertEqual(180, kwargs["timeout"])
        self.assertEqual("subscription", command[command.index("--subscription") + 1])
        self.calls.append(command)
        operation = tuple(command[1:next(
            index for index, value in enumerate(command) if value.startswith("-")
        )])
        if operation == self.timeout_operation:
            raise subprocess.TimeoutExpired(command, kwargs["timeout"])
        if operation == self.fail_operation:
            return subprocess.CompletedProcess(command, 1, "", "ERROR: Unauthorized")
        responses = {
            ("iot", "dps", "show"): self.scope,
            ("iot", "dps", "enrollment-group", "show"): {
                "id": "group", "namespace": self.enrollment_namespace,
            },
            ("iot", "dps", "enrollment-group", "registration", "list"): ["sorter"],
            ("iot", "hub", "show"): "hub",
            ("iot", "dps", "enrollment-group", "registration", "show"): {
                "id": "sorter", "device": self.assigned_device, "hub": self.assigned_hub,
            },
            ("iot", "adr", "ns", "device", "list"): ["sorter"],
            ("iot", "adr", "ns", "device", "delete"): None,
            ("iot", "adr", "ns", "device", "wait"): None,
            ("iot", "hub", "device-identity", "delete"): None,
            ("iot", "dps", "enrollment-group", "registration", "delete"): None,
        }
        if operation == ("iot", "hub", "device-identity", "show"):
            self.hub_checks += 1
            if self.cascade and self.hub_checks > 1:
                return subprocess.CompletedProcess(
                    command, 3, "", "ERROR: ErrorCode:DeviceNotFound;404",
                )
            response = "sorter"
        else:
            self.assertIn(operation, responses)
            response = responses[operation]
        return subprocess.CompletedProcess(
            command, 0, "" if response is None else json.dumps(response), "",
        )

    def run_reset(self, options=(), confirm="y"):
        output = io.StringIO()
        with patch.object(clean_test_start.subprocess, "run", side_effect=self.azure), \
             patch("builtins.input", return_value=confirm), \
             contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            code = clean_test_start.main([
                "--config", str(self.config), "--dps-name", "dps",
                "--hub-name", "hub", "--enrollment-id", "group", *options,
            ])
        for path in (self.issued, self.key, self.csr):
            self.assertEqual("retained", path.read_text())
        return code, output.getvalue()

    def test_key_reset_targets_only_configured_device_and_retains_credentials(self):
        code, output = self.run_reset(["--hub-auth-type", "key"])
        self.assertEqual(0, code)
        self.assertFalse(self.cache.exists())
        self.assertIn("Clean demo start prepared for 'sorter'", output)
        hub_commands = [c for c in self.calls if c[1:4] == ["iot", "hub", "device-identity"]]
        self.assertEqual(3, len(hub_commands))
        for command in hub_commands:
            self.assertEqual("key", command[command.index("--auth-type") + 1])
            self.assertEqual("sorter", command[command.index("--device-id") + 1])
        adr_delete = next(c for c in self.calls if c[1:6] == ["iot", "adr", "ns", "device", "delete"])
        self.assertIn("--no-wait", adr_delete)
        wait = next(c for c in self.calls if c[1:6] == ["iot", "adr", "ns", "device", "wait"])
        self.assertIn("--deleted", wait)
        self.assertEqual("120", wait[wait.index("--timeout") + 1])
        self.assertLess(self.calls.index(wait), self.calls.index(hub_commands[-1]))

    def test_default_login_handles_cascaded_hub_deletion(self):
        self.cascade = True
        code, _ = self.run_reset()
        self.assertEqual(0, code)
        self.assertFalse(self.cache.exists())
        hub_commands = [c for c in self.calls if c[1:4] == ["iot", "hub", "device-identity"]]
        self.assertEqual(2, len(hub_commands))
        for command in hub_commands:
            self.assertEqual("login", command[command.index("--auth-type") + 1])

    def test_mismatched_scope_namespace_device_or_hub_stops_before_deletion(self):
        for field in ("scope", "enrollment_namespace", "assigned_device", "assigned_hub"):
            with self.subTest(field=field):
                original = getattr(self, field)
                setattr(self, field, "wrong")
                self.calls = []
                code, _ = self.run_reset(["--yes"])
                self.assertEqual(1, code)
                self.assertTrue(self.cache.exists())
                self.assertFalse(any("delete" in c for c in self.calls))
                setattr(self, field, original)

    def test_unauthorized_hub_is_not_treated_as_missing_or_retried_with_key(self):
        self.fail_operation = ("iot", "hub", "device-identity", "show")
        code, output = self.run_reset(["--yes"])
        self.assertEqual(1, code)
        self.assertIn("Unauthorized", output)
        self.assertTrue(self.cache.exists())
        self.assertFalse(any("delete" in c for c in self.calls))
        self.assertFalse(any("key" in c for c in self.calls))

    def test_timeout_during_adr_wait_preserves_cache_and_stops_remaining_cleanup(self):
        self.timeout_operation = ("iot", "adr", "ns", "device", "wait")
        code, output = self.run_reset(["--yes"])
        self.assertEqual(1, code)
        self.assertIn("180s command timeout", output)
        self.assertTrue(self.cache.exists())
        self.assertFalse(any(c[1:5] == ["iot", "hub", "device-identity", "delete"] for c in self.calls))
        self.assertFalse(any(c[1:6] == ["iot", "dps", "enrollment-group", "registration", "delete"] for c in self.calls))

    def test_preview_and_cancel_do_not_delete(self):
        code, _ = self.run_reset(["--what-if"])
        self.assertEqual(0, code)
        self.assertEqual([], self.calls)
        code, _ = self.run_reset(confirm="n")
        self.assertEqual(0, code)
        self.assertTrue(self.cache.exists())
        self.assertFalse(any("delete" in c for c in self.calls))


if __name__ == "__main__":
    unittest.main()
