import contextlib
import io
import importlib.util
import os
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest
from pathlib import Path, PurePosixPath
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "installer_helper", ROOT / "deploy" / "updates" / "installer-helper.py"
)
helper = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(helper)


class HelperArgumentBoundaryTests(unittest.TestCase):
    def test_installer_command_is_fixed_and_root_path_cannot_be_overridden(self):
        captured = {}

        def runner(arguments, **kwargs):
            captured["arguments"] = arguments
            captured["kwargs"] = kwargs

        with patch.object(helper, "PYTHON_PATH", "/usr/bin/python3"), patch.object(
            helper, "INSTALLER_PATH", "/usr/lib/skittlesorter/installer.py"
        ):
            helper._run_install(
                PurePosixPath("/var/lib/skittlesorter-installer/payloads/payload.tar.gz"),
                "1.1.0",
                "linux-arm64",
                runner=runner,
            )

        self.assertEqual(
            captured["arguments"],
            [
                "/usr/bin/python3",
                "/usr/lib/skittlesorter/installer.py",
                "install",
                "--root",
                "/opt/skittlesorter",
                "--payload",
                "/var/lib/skittlesorter-installer/payloads/payload.tar.gz",
                "--version",
                "1.1.0",
                "--runtime",
                "linux-arm64",
                "--ready-file",
                "/var/lib/skittlesorter/ready.json",
            ],
        )
        self.assertEqual(captured["kwargs"]["cwd"], "/")
        self.assertEqual(captured["kwargs"]["timeout"], 300)
        self.assertEqual(captured["kwargs"]["env"]["PATH"], "/usr/bin:/bin")
        self.assertFalse(captured["kwargs"].get("shell", False))

    def test_cli_rejects_any_attempt_to_override_fixed_installer_root(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            helper.main(
                [
                    "install",
                    "--payload",
                    "1.1.0-demo.tar.gz",
                    "--version",
                    "1.1.0",
                    "--runtime",
                    "linux-arm64",
                    "--root",
                    "/not-allowed/root",
                ]
            )

    def test_cli_parser_does_not_accept_non_allowlisted_operation_or_runtime(self):
        for arguments in (
            ["recover", "--payload", "release.tar.gz", "--version", "1.1.0", "--runtime", "linux-arm64"],
            ["install", "--payload", "release.tar.gz", "--version", "1.1.0", "--runtime", "linux-x64"],
        ):
            with self.subTest(arguments=arguments), contextlib.redirect_stderr(
                io.StringIO()
            ), self.assertRaises(SystemExit):
                helper.main(arguments)


@unittest.skipUnless(os.name == "posix", "snapshot tests require POSIX file descriptors")
class SnapshotBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=str(ROOT / "tests" / "installer"))
        self.base = Path(self.temp.name)
        self.source = self.base / "source"
        self.snapshot = self.base / "root-owned" / "payloads"
        self.source.mkdir(mode=0o700)
        self.snapshot.mkdir(parents=True, mode=0o700)
        os.chmod(self.source, 0o700)
        os.chmod(self.snapshot.parent, 0o700)
        os.chmod(self.snapshot, 0o700)
        self.payload = self.source / "release-1.1.0.tar.gz"
        self.payload.write_bytes(b"verified archive bytes")
        os.chmod(self.payload, 0o600)
        self._patch_constants()
        self.chain_validator = patch.object(
            helper,
            "_open_secure_directory",
            side_effect=self._open_fixture_directory,
        )
        self.chain_validator.start()
        self.addCleanup(self.chain_validator.stop)

    @staticmethod
    def _open_fixture_directory(path, expected_uid):
        info = os.lstat(path)
        if not os.path.isdir(path) or os.path.islink(path) or info.st_uid != expected_uid:
            raise helper.HelperError(f"Unsafe test directory: {path}")
        return os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))

    def tearDown(self):
        self.temp.cleanup()

    def _patch_constants(self):
        patcher = patch.multiple(
            helper,
            PAYLOAD_DIRECTORY=self.source,
            SNAPSHOT_DIRECTORY=self.snapshot,
        )
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_secure_directory_opener_rejects_symlinked_directory(self):
        linked_directory = self.base / "source-link"
        linked_directory.symlink_to(self.source, target_is_directory=True)
        with self.assertRaises(helper.HelperError):
            helper._open_secure_directory(linked_directory, os.getuid())

    def test_snapshots_payload_before_install_and_cleans_after_install(self):
        snapshots_seen_by_installer = []

        def runner(arguments, **_kwargs):
            snapshot = Path(arguments[arguments.index("--payload") + 1])
            snapshots_seen_by_installer.append(snapshot)
            self.assertEqual(snapshot.read_bytes(), b"verified archive bytes")
            self.assertEqual(snapshot.stat().st_uid, os.getuid())

        with patch.object(helper.os, "geteuid", return_value=0):
            with patch.object(helper.os, "fchmod", wraps=helper.os.fchmod) as chmod:
                original_lstat = helper.os.lstat

                def test_lstat(path):
                    result = original_lstat(path)
                    if Path(path).parent == self.snapshot:
                        return SimpleNamespace(
                            st_mode=stat.S_IFREG | 0o400,
                            st_uid=os.getuid(),
                            st_nlink=1,
                            st_size=result.st_size,
                        )
                    return result

                with patch.object(helper.os, "lstat", side_effect=test_lstat):
                    helper.install(
                        self.payload.name,
                        "1.1.0",
                        "linux-arm64",
                        runner=runner,
                        expected_uid=os.getuid(),
                        snapshot_uid=os.getuid(),
                    )
                chmod.assert_called_once()
                self.assertEqual(chmod.call_args.args[1], 0o400)

        self.assertEqual(len(snapshots_seen_by_installer), 1)
        self.assertFalse(snapshots_seen_by_installer[0].exists())

    def test_rejects_path_traversal_and_symlink_payloads(self):
        with self.assertRaisesRegex(helper.HelperError, "basename"):
            helper._snapshot_payload("../release-1.1.0.tar.gz", os.getuid(), snapshot_uid=os.getuid())
        link = self.source / "linked.tar.gz"
        link.symlink_to(self.payload.name)

        with self.assertRaises(helper.HelperError):
            helper._snapshot_payload(link.name, os.getuid(), snapshot_uid=os.getuid())

    def test_rejects_payload_that_changes_during_snapshot(self):
        original_copy = helper._copy_descriptor

        def mutate_after_copy(source_fd, destination_fd):
            copied = original_copy(source_fd, destination_fd)
            self.payload.write_bytes(b"changed while root copied")
            return copied

        with patch.object(helper, "_copy_descriptor", side_effect=mutate_after_copy):
            with self.assertRaisesRegex(helper.HelperError, "changed during"):
                helper._snapshot_payload(self.payload.name, os.getuid(), snapshot_uid=os.getuid())
        self.assertEqual(list(self.snapshot.iterdir()), [])

    def test_rejects_oversized_payload_before_installer_execution(self):
        with patch.object(helper, "MAX_PAYLOAD_BYTES", 4):
            with self.assertRaisesRegex(helper.HelperError, "compressed-size limit"):
                helper._snapshot_payload(self.payload.name, os.getuid(), snapshot_uid=os.getuid())
        self.assertEqual(list(self.snapshot.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
