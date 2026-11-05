import base64
import hashlib
import importlib.util
import json
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


packager = load("packager", "scripts/updates/common/package-release.py")
manifests = load("manifests", "scripts/updates/common/new-import-manifest.py")
adapter = load("adapter", "scripts/updates/device-update/install-sorter.py")


class ReleaseTests(unittest.TestCase):
    def test_release_preserves_executable_mode_and_feature_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publish = root / "publish"
            publish.mkdir()
            (publish / "skittlesorter").write_bytes(b"fixture")
            (publish / "sorter.dll").write_bytes(b"fixture")
            target = packager.package(publish, root, "1.1.0", "linux-arm64", True)
            with tarfile.open(target) as archive:
                self.assertEqual(0o755, archive.getmember("skittlesorter").mode)
                self.assertEqual(
                    {"version": "1.1.0", "runtime": "linux-arm64", "features": ["session-counts"]},
                    json.load(archive.extractfile("release.json")))

    def test_credentials_cannot_accidentally_enter_a_payload(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "skittlesorter").write_bytes(b"fixture")
            for name in ("appsettings.json", "device.pem", "device.key"):
                with self.subTest(name=name):
                    secret = root / name
                    secret.write_text("not a real secret")
                    with self.assertRaises(ValueError):
                        packager.package(root, root / "out", "1.0.0", "linux-arm", False)
                    secret.unlink()

    def test_channel_manifests_hash_actual_payload_and_use_distinct_handlers(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            payload = root / "sorter.tar.gz"
            payload.write_bytes(b"payload fixture")
            for channel, handler, file_count in (
                    ("ga", "microsoft/script:1", 2),
                    ("adr", "skittlesorter/application:1", 1)):
                manifest = manifests.generate(
                    payload, "1.1.0", "linux-arm64", channel, root / (channel + ".json"))
                self.assertEqual(handler, manifest["instructions"]["steps"][0]["handler"])
                self.assertEqual(file_count, len(manifest["files"]))
                self.assertEqual(base64.b64encode(hashlib.sha256(payload.read_bytes()).digest()).decode(),
                                 manifest["files"][0]["hashes"]["sha256"])
                self.assertEqual("linux-arm64", manifest["compatibility"][0]["architecture"])

    def test_ga_adapter_delegates_only_to_fixed_local_installer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = root / "result.json"
            with mock.patch.object(adapter.subprocess, "run") as run:
                code = adapter.main([
                    "--action-install", "--payload", "sorter.tar.gz", "--version", "1.1.0",
                    "--runtime", "linux-arm64", "--installed-criteria", "1.1.0",
                    "--work-folder", str(root), "--result-file", str(result)])
                self.assertEqual(0, code)
                self.assertEqual("/usr/lib/skittlesorter/installer.py", run.call_args.args[0][1])
                self.assertEqual(600, json.loads(result.read_text())["resultCode"])
                self.assertNotIn("shell", run.call_args.kwargs)

    def test_ga_manifest_rejects_shell_metacharacters_in_payload_arguments(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            payload = root / "payload;command.tar.gz"
            payload.write_bytes(b"fixture")
            with self.assertRaises(ValueError):
                manifests.generate(payload, "1.1.0", "linux-arm64", "ga", root / "manifest.json")
            self.assertFalse((root / "manifest.json").exists())

    def test_ga_adapter_rejects_traversal_and_reports_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = root / "result.json"
            with mock.patch.object(adapter.subprocess, "run") as run:
                code = adapter.main([
                    "--action-install", "--payload", "../sorter.tar.gz", "--version", "1.1.0",
                    "--runtime", "linux-arm64", "--installed-criteria", "1.1.0",
                    "--work-folder", str(root), "--result-file", str(result)])
                self.assertEqual(1, code)
                self.assertEqual(0, json.loads(result.read_text())["resultCode"])
                run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
