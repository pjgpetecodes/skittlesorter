#!/usr/bin/python3 -I
"""Root-owned, constrained privilege boundary for the Skittle Sorter installer."""

import argparse
import errno
import os
import re
import stat
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    import pwd
except ImportError:  # pragma: no cover - enables parsing/contract tests on Windows
    pwd = None


SERVICE_ACCOUNT = "skittlesorter"
PAYLOAD_DIRECTORY = Path("/var/cache/skittlesorter-updates/incoming")
SNAPSHOT_DIRECTORY = Path("/var/lib/skittlesorter-installer/payloads")
INSTALLER_PATH = "/usr/lib/skittlesorter/installer.py"
PYTHON_PATH = "/usr/bin/python3"
INSTALL_ROOT = "/opt/skittlesorter"
READY_FILE = "/var/lib/skittlesorter/ready.json"
MAX_PAYLOAD_BYTES = 256 * 1024 * 1024
PAYLOAD_NAME_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,180}\.tar\.gz\Z")
VERSION_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\Z")
ALLOWED_RUNTIMES = {"linux-arm", "linux-arm64"}


class HelperError(RuntimeError):
    pass


def _open_secure_directory(path, leaf_uid):
    path = Path(path)
    if not path.is_absolute() or any(part in {".", ".."} for part in path.parts[1:]):
        raise HelperError(f"Directory must be an absolute normalized path: {path}")
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
    current_fd = os.open("/", flags)
    components = path.parts[1:]
    try:
        for index, part in enumerate(components):
            next_fd = os.open(part, flags, dir_fd=current_fd)
            info = os.fstat(next_fd)
            final_component = index == len(components) - 1
            owner_ok = info.st_uid == leaf_uid if final_component else info.st_uid in {0, leaf_uid}
            if (
                not stat.S_ISDIR(info.st_mode)
                or not owner_ok
                or stat.S_IMODE(info.st_mode) & 0o022
            ):
                os.close(next_fd)
                raise HelperError(f"Unsafe directory component: {path}")
            os.close(current_fd)
            current_fd = next_fd
        if not components:
            raise HelperError("Filesystem root cannot be used as an installer directory")
        return current_fd
    except Exception:
        os.close(current_fd)
        raise


def _source_stat_matches(left, right):
    return (
        left.st_dev == right.st_dev
        and left.st_ino == right.st_ino
        and left.st_size == right.st_size
        and left.st_mtime_ns == right.st_mtime_ns
        and left.st_ctime_ns == right.st_ctime_ns
    )


def _copy_descriptor(source_fd, destination):
    copied = 0
    while True:
        chunk = os.read(source_fd, 1024 * 1024)
        if not chunk:
            break
        copied += len(chunk)
        if copied > MAX_PAYLOAD_BYTES:
            raise HelperError("Payload exceeds the helper's compressed-size limit")
        view = memoryview(chunk)
        while view:
            written = os.write(destination, view)
            view = view[written:]
    return copied


def _snapshot_payload(name, expected_uid, snapshot_directory=None, snapshot_uid=0):
    if not PAYLOAD_NAME_PATTERN.fullmatch(name):
        raise HelperError("Payload must be a basename ending in .tar.gz")
    source_directory = PAYLOAD_DIRECTORY
    snapshot_directory = Path(snapshot_directory or SNAPSHOT_DIRECTORY)
    directory_fd = _open_secure_directory(source_directory, expected_uid)
    snapshot_dir_fd = None
    source_fd = None
    snapshot_path = None
    snapshot_complete = False
    try:
        snapshot_dir_fd = _open_secure_directory(snapshot_directory, snapshot_uid)
        flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
        source_fd = os.open(name, flags, dir_fd=directory_fd)
        before = os.fstat(source_fd)
        if not stat.S_ISREG(before.st_mode) or before.st_uid != expected_uid or before.st_nlink != 1:
            raise HelperError("Payload must be a single-link regular file owned by the updater account")
        if before.st_size <= 0 or before.st_size > MAX_PAYLOAD_BYTES:
            raise HelperError("Payload exceeds the helper's compressed-size limit")

        source_path_info = os.stat(name, dir_fd=directory_fd, follow_symlinks=False)
        if not _source_stat_matches(before, source_path_info):
            raise HelperError("Payload path changed while it was being opened")

        snapshot_fd, snapshot_name = tempfile.mkstemp(
            prefix="payload-", suffix=".tar.gz", dir=str(snapshot_directory)
        )
        snapshot_path = Path(snapshot_name)
        try:
            os.fchmod(snapshot_fd, 0o400)
            copied = _copy_descriptor(source_fd, snapshot_fd)
            os.fsync(snapshot_fd)
        finally:
            os.close(snapshot_fd)

        after = os.fstat(source_fd)
        source_path_info = os.stat(name, dir_fd=directory_fd, follow_symlinks=False)
        if not _source_stat_matches(before, after) or not _source_stat_matches(after, source_path_info):
            raise HelperError("Payload changed during the root-owned snapshot")
        if copied != before.st_size:
            raise HelperError("Payload changed size during the root-owned snapshot")
        snapshot_info = os.lstat(snapshot_path)
        if (
            not stat.S_ISREG(snapshot_info.st_mode)
            or snapshot_info.st_uid != snapshot_uid
            or stat.S_IMODE(snapshot_info.st_mode) != 0o400
            or snapshot_info.st_nlink != 1
            or snapshot_info.st_size != copied
        ):
            raise HelperError("Unable to create a safe root-owned payload snapshot")
        snapshot_complete = True
        return snapshot_path
    except OSError as exc:
        if exc.errno in {errno.ELOOP, errno.ENOTDIR}:
            raise HelperError("Payload path must not contain symlinks") from exc
        raise HelperError(f"Unable to snapshot payload: {exc}") from exc
    finally:
        if source_fd is not None:
            os.close(source_fd)
        os.close(directory_fd)
        if snapshot_dir_fd is not None:
            os.close(snapshot_dir_fd)
        if not snapshot_complete and snapshot_path is not None:
            try:
                snapshot_path.unlink()
            except FileNotFoundError:
                pass


def _run_install(snapshot, version, runtime, runner=subprocess.run):
    return runner(
        [
            PYTHON_PATH,
            INSTALLER_PATH,
            "install",
            "--root",
            INSTALL_ROOT,
            "--payload",
            str(snapshot),
            "--version",
            version,
            "--runtime",
            runtime,
            "--ready-file",
            READY_FILE,
        ],
        check=True,
        cwd="/",
        env={"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8"},
        close_fds=True,
        timeout=300,
    )


def install(name, version, runtime, runner=subprocess.run, expected_uid=None, snapshot_uid=0):
    if os.geteuid() != 0:
        raise HelperError("This helper must run as root")
    if not VERSION_PATTERN.fullmatch(version):
        raise HelperError("Invalid version string")
    if runtime not in ALLOWED_RUNTIMES:
        raise HelperError("Unsupported runtime")
    if expected_uid is None:
        if pwd is None:
            raise HelperError("POSIX account lookup is required")
        try:
            expected_uid = pwd.getpwnam(SERVICE_ACCOUNT).pw_uid
        except KeyError as exc:
            raise HelperError(f"Service account {SERVICE_ACCOUNT!r} does not exist") from exc

    snapshot = _snapshot_payload(name, expected_uid, snapshot_uid=snapshot_uid)
    try:
        _run_install(snapshot, version, runtime, runner=runner)
    finally:
        try:
            snapshot.unlink()
        except FileNotFoundError:
            pass


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["install"])
    parser.add_argument("--payload", required=True, help="Basename within the fixed updater download directory")
    parser.add_argument("--version", required=True)
    parser.add_argument("--runtime", required=True, choices=sorted(ALLOWED_RUNTIMES))
    args = parser.parse_args(argv)
    try:
        install(args.payload, args.version, args.runtime)
        print(f"Installed Skittle Sorter {args.version}")
        return 0
    except (HelperError, OSError, subprocess.SubprocessError) as exc:
        print(f"Skittle Sorter installation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
