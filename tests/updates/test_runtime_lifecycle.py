"""Optional real-process smoke test: point SKITTLESORTER_TEST_EXECUTABLE at a Linux build."""
import json
import os
import signal
import subprocess
import tempfile
import time
import unittest
from pathlib import Path


@unittest.skipUnless(os.name == "posix" and os.environ.get("SKITTLESORTER_TEST_EXECUTABLE"),
                     "Requires a Linux sorter executable; never run against real hardware configuration.")
class RuntimeLifecycleTests(unittest.TestCase):
    def test_sigterm_completes_current_drop_before_exiting(self):
        executable = os.environ["SKITTLESORTER_TEST_EXECUTABLE"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            config = root / "appsettings.json"
            readiness = root / "ready.json"
            config.write_text(json.dumps({
                "Hardware": {
                    "MockMode": {"EnableMockColorSensor": True, "EnableMockServos": True,
                                 "MockColorSequence": ["Red"]}},
                "IoTHub": {"SendTelemetry": False}}))
            process = subprocess.Popen(
                [executable, "--config", str(config), "--ready-file", str(readiness)],
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
            try:
                deadline = time.monotonic() + 15
                while not readiness.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.01)
                self.assertTrue(readiness.exists(), "Application did not become ready.")
                running = json.loads(readiness.read_text())
                self.assertEqual(process.pid, running["processId"])
                if os.environ.get("SKITTLESORTER_EXPECT_VERSION"):
                    self.assertEqual(os.environ["SKITTLESORTER_EXPECT_VERSION"], running["version"])
                # Readiness is emitted just before the cycle starts; SIGTERM arrives during pick.
                time.sleep(0.1)
                process.send_signal(signal.SIGTERM)
                output, _ = process.communicate(timeout=20)
                self.assertEqual(0, process.returncode, output)
                self.assertEqual(1, output.count("Sorted."), output)
                if os.environ.get("SKITTLESORTER_EXPECT_COUNTS", "true") == "true":
                    self.assertIn("total=1", output)
                else:
                    self.assertNotIn("Session count:", output)
                self.assertFalse(readiness.exists())
            finally:
                if process.poll() is None:
                    process.kill()
                    process.communicate()
