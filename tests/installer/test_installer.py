import io
import json
import os
import sys
import tarfile
import tempfile
import unittest
from collections import namedtuple
from datetime import datetime, timezone
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "deploy" / "updates"))
import installer  # noqa: E402


def _archive(path, version="1.1.0", runtime="linux-arm64", entries=None):
    if entries is None:
        entries = [
            ("skittlesorter", b"executable payload", 0o755, tarfile.REGTYPE, ""),
            (
                "release.json",
                json.dumps({"version": version, "runtime": runtime, "features": ["SessionCountsEnabled"]}).encode(),
                0o644,
                tarfile.REGTYPE,
                "",
            ),
        ]
    with tarfile.open(path, "w:gz") as archive:
        for name, body, mode, kind, linkname in entries:
            member = tarfile.TarInfo(name)
            member.mode = mode
            member.type = kind
            member.linkname = linkname
            member.size = len(body) if kind == tarfile.REGTYPE else 0
            archive.addfile(member, io.BytesIO(body) if member.size else None)
    return path


def _release(root, version):
    path = root / "releases" / version
    path.mkdir(parents=True)
    executable = path / "skittlesorter"
    executable.write_bytes(b"existing executable")
    executable.chmod(0o755)
    (path / "release.json").write_text(
        json.dumps({"version": version, "runtime": "linux-arm64", "features": []}),
        encoding="utf-8",
    )
    return path


class FakeService:
    def __init__(self, current, ready_file, initially_active=False, failing_versions=()):
        self.current = current
        self.ready_file = ready_file
        self.active = initially_active
        self.failing_versions = set(failing_versions)
        self.starts = 0
        self.stops = 0
        self.ready_was_removed_before_start = True

    def stop(self):
        self.stops += 1
        self.active = False

    def start(self):
        self.starts += 1
        self.ready_was_removed_before_start &= not self.ready_file.exists()
        self.active = True
        try:
            release = self.current.resolve(strict=True)
            version = json.loads((release / "release.json").read_text(encoding="utf-8"))["version"]
        except (OSError, ValueError):
            return
        if version in self.failing_versions:
            return
        self.ready_file.parent.mkdir(parents=True, exist_ok=True)
        self.ready_file.write_text(
            json.dumps(
                {
                    "version": version,
                    "processId": 4242,
                    "readyAt": datetime.now(timezone.utc).isoformat(),
                }
            ),
            encoding="utf-8",
        )

    def is_active(self):
        return self.active

    def main_pid(self):
        return 4242

    def process_matches_release(self, pid, executable):
        return pid == 4242 and executable.is_file()


@unittest.skipUnless(hasattr(os, "symlink"), "symlink support required")
class InstallerIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name)
        self.root = self.base / "opt" / "skittlesorter"
        self.ready = self.base / "var" / "lib" / "skittlesorter" / "ready.json"
        self.root.mkdir(parents=True)
        (self.root / "releases").mkdir()

    def tearDown(self):
        self.temp.cleanup()

    def _service(self, **kwargs):
        return FakeService(self.root / "current", self.ready, **kwargs)

    def _run_install(self, archive, service, version="1.1.0", runtime="linux-arm64"):
        return installer.Installer(self.root, self.ready, service).install(archive, version, runtime)

    def test_installs_and_commits_only_after_fresh_versioned_readiness(self):
        old = _release(self.root, "1.0.0")
        installer._atomic_symlink("releases/1.0.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        config = self.base / "etc" / "appsettings.json"
        cert = self.base / "etc" / "device.pem"
        config.parent.mkdir()
        config.write_bytes(b"external config must persist")
        cert.write_bytes(b"external certificate must persist")
        self.ready.parent.mkdir(parents=True)
        self.ready.write_text(
            json.dumps(
                {
                    "version": "1.0.0",
                    "processId": 111,
                    "readyAt": "2000-01-01T00:00:00+00:00",
                }
            ),
            encoding="utf-8",
        )
        service = self._service(initially_active=True)
        payload = _archive(self.base / "release.tar.gz")

        result = self._run_install(payload, service)

        self.assertEqual(result, "1.1.0")
        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.1.0")
        self.assertEqual((self.root / "last-good").resolve(), self.root / "releases" / "1.1.0")
        self.assertEqual(json.loads((self.root / "releases" / "1.1.0" / "release.json").read_text())["version"], "1.1.0")
        self.assertEqual(config.read_bytes(), b"external config must persist")
        self.assertEqual(cert.read_bytes(), b"external certificate must persist")
        self.assertTrue(service.ready_was_removed_before_start)
        self.assertFalse((self.root / ".installer-journal.json").exists())
        self.assertTrue(old.is_dir())

    def test_failed_health_check_restores_and_restarts_last_good(self):
        _release(self.root, "1.0.0")
        installer._atomic_symlink("releases/1.0.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=True, failing_versions={"1.1.0"})
        payload = _archive(self.base / "release.tar.gz")

        with patch.object(installer, "HEALTH_TIMEOUT_SECONDS", 0.02), patch.object(
            installer, "HEALTH_POLL_SECONDS", 0.001
        ):
            with self.assertRaisesRegex(installer.InstallError, "fresh readiness"):
                self._run_install(payload, service)

        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.0.0")
        self.assertEqual((self.root / "last-good").resolve(), self.root / "releases" / "1.0.0")
        self.assertTrue(service.active)
        self.assertEqual(service.starts, 2)
        self.assertFalse((self.root / ".installer-journal.json").exists())

    def test_activation_journal_recovers_to_last_good_after_interruption(self):
        _release(self.root, "1.0.0")
        _release(self.root, "1.1.0")
        installer._atomic_symlink("releases/1.1.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=False)
        self.root.joinpath(".installer-journal.json").write_text(
            json.dumps(
                {
                    "phase": "activated",
                    "target": "1.1.0",
                    "previous": "1.0.0",
                    "started_at": 0,
                }
            ),
            encoding="utf-8",
        )

        installer.Installer(self.root, self.ready, service).recover()

        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.0.0")
        self.assertEqual((self.root / "last-good").resolve(), self.root / "releases" / "1.0.0")
        self.assertTrue(service.active)
        self.assertFalse((self.root / ".installer-journal.json").exists())

    def test_boot_recovery_restores_last_good_before_service_start(self):
        _release(self.root, "1.0.0")
        _release(self.root, "1.1.0")
        installer._atomic_symlink("releases/1.1.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=False)
        self.ready.parent.mkdir(parents=True)
        self.ready.write_text('{"version":"1.1.0","processId":4242}', encoding="utf-8")
        self.root.joinpath(".installer-journal.json").write_text(
            json.dumps(
                {
                    "phase": "activated",
                    "target": "1.1.0",
                    "previous": "1.0.0",
                    "started_at": 0,
                }
            ),
            encoding="utf-8",
        )

        installer.Installer(self.root, self.ready, service).recover_at_boot()

        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.0.0")
        self.assertEqual((self.root / "last-good").resolve(), self.root / "releases" / "1.0.0")
        self.assertFalse((self.root / "releases" / "1.1.0").exists())
        self.assertFalse(self.ready.exists())
        self.assertEqual(service.starts, 0)
        self.assertFalse((self.root / ".installer-journal.json").exists())

    def test_startup_recovery_skips_live_installer_transaction_without_blocking(self):
        _release(self.root, "1.0.0")
        _release(self.root, "1.1.0")
        installer._atomic_symlink("releases/1.1.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=False)
        transaction = self.root / ".installer-journal.json"
        transaction.write_text(
            json.dumps(
                {
                    "phase": "activated",
                    "target": "1.1.0",
                    "previous": "1.0.0",
                    "started_at": 1,
                }
            ),
            encoding="utf-8",
        )
        startup = installer.Installer(self.root, self.ready, service)

        with installer.InstallLock(startup.lock_path):
            self.assertFalse(startup.recover_at_boot())

        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.1.0")
        self.assertTrue(transaction.exists())

    def test_health_verified_journal_finishes_commit_after_interruption(self):
        _release(self.root, "1.0.0")
        _release(self.root, "1.1.0")
        installer._atomic_symlink("releases/1.1.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=True)
        self.root.joinpath(".installer-journal.json").write_text(
            json.dumps(
                {
                    "phase": "health_verified",
                    "target": "1.1.0",
                    "previous": "1.0.0",
                    "started_at": 1,
                }
            ),
            encoding="utf-8",
        )

        installer.Installer(self.root, self.ready, service).recover()

        self.assertEqual((self.root / "last-good").resolve(), self.root / "releases" / "1.1.0")
        self.assertEqual(service.stops, 0)
        self.assertFalse((self.root / ".installer-journal.json").exists())

    def test_rejects_runtime_mismatch_before_service_stop(self):
        payload = _archive(self.base / "release.tar.gz", runtime="linux-arm")
        service = self._service()

        with self.assertRaisesRegex(installer.InstallError, "do not match"):
            self._run_install(payload, service, runtime="linux-arm64")

        self.assertEqual(service.stops, 0)
        self.assertFalse((self.root / "current").exists())

    def test_candidate_is_never_executed_by_privileged_installer(self):
        payload = _archive(self.base / "release.tar.gz")
        service = self._service()

        with patch.object(installer.subprocess, "run") as process_runner:
            self._run_install(payload, service)

        # Candidate execution belongs exclusively to the systemd User=skittlesorter service.
        process_runner.assert_not_called()
        self.assertEqual(service.starts, 1)

    def test_service_manager_launches_only_fixed_systemctl_commands(self):
        service = installer.ServiceManager()

        with patch.object(installer.subprocess, "run") as process_runner:
            service.stop()
            service.start()

        self.assertEqual(
            [call.args[0] for call in process_runner.call_args_list],
            [
                ["systemctl", "stop", "skittlesorter"],
                ["systemctl", "start", "skittlesorter"],
            ],
        )

    def test_rejects_expanded_payload_over_limit_before_service_stop(self):
        payload = _archive(
            self.base / "large-release.tar.gz",
            entries=[
                ("skittlesorter", b"executable", 0o755, tarfile.REGTYPE, ""),
                ("release.json", b'{"version":"1.1.0","runtime":"linux-arm64","features":[]}', 0o644, tarfile.REGTYPE, ""),
                ("large.bin", b"0123456789", 0o644, tarfile.REGTYPE, ""),
            ],
        )
        service = self._service()

        with patch.object(installer, "MAX_RELEASE_BYTES", 8):
            with self.assertRaisesRegex(installer.InstallError, "expanded-size"):
                self._run_install(payload, service)

        self.assertEqual(service.stops, 0)

    def test_rejects_insufficient_disk_space_before_service_stop(self):
        payload = _archive(self.base / "release.tar.gz")
        service = self._service()
        disk_usage = namedtuple("DiskUsage", "total used free")

        with patch.object(installer.shutil, "disk_usage", return_value=disk_usage(100, 100, 0)):
            with self.assertRaisesRegex(installer.InstallError, "Insufficient free space"):
                self._run_install(payload, service)

        self.assertEqual(service.stops, 0)
        self.assertFalse((self.root / "current").exists())

    def test_safe_stop_failure_aborts_and_keeps_last_good_running(self):
        _release(self.root, "1.0.0")
        installer._atomic_symlink("releases/1.0.0", self.root / "current")
        installer._atomic_symlink("releases/1.0.0", self.root / "last-good")
        service = self._service(initially_active=True)
        stop = service.stop
        failures = [True]

        def fail_first_stop():
            if failures:
                failures.pop()
                raise installer.InstallError("safe stop timed out")
            stop()

        service.stop = fail_first_stop
        payload = _archive(self.base / "release.tar.gz")

        with self.assertRaisesRegex(installer.InstallError, "safe stop timed out"):
            self._run_install(payload, service)

        self.assertEqual((self.root / "current").resolve(), self.root / "releases" / "1.0.0")
        self.assertTrue(service.active)
        self.assertFalse((self.root / ".installer-journal.json").exists())

    def test_rejects_traversal_symlink_and_duplicate_archive_paths(self):
        malformed = [
            [("../escape", b"x", 0o644, tarfile.REGTYPE, "")],
            [("evil-link", b"", 0o777, tarfile.SYMTYPE, "../outside")],
            [
                ("skittlesorter", b"first", 0o755, tarfile.REGTYPE, ""),
                ("./skittlesorter", b"second", 0o755, tarfile.REGTYPE, ""),
            ],
        ]
        for index, entries in enumerate(malformed):
            with self.subTest(index=index):
                payload = _archive(self.base / f"bad-{index}.tar.gz", entries=entries)
                service = self._service()
                with self.assertRaises(installer.InstallError):
                    self._run_install(payload, service)
                self.assertEqual(service.stops, 0)
                self.assertFalse((self.root / "current").exists())

    def test_rejects_external_configuration_and_private_key_members(self):
        payload = _archive(
            self.base / "bad-config.tar.gz",
            entries=[
                ("skittlesorter", b"executable", 0o755, tarfile.REGTYPE, ""),
                ("release.json", b'{"version":"1.1.0","runtime":"linux-arm64","features":[]}', 0o644, tarfile.REGTYPE, ""),
                ("appsettings.json", b"secret config", 0o600, tarfile.REGTYPE, ""),
            ],
        )
        service = self._service()
        with self.assertRaisesRegex(installer.InstallError, "configuration/secret"):
            self._run_install(payload, service)
        self.assertEqual(service.stops, 0)


if __name__ == "__main__":
    unittest.main()
