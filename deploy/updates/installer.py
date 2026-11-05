#!/usr/bin/env python3
"""Transactional installer for signed/verified Skittle Sorter release payloads.

Signature verification belongs to the calling update agent. This program only
accepts a bounded, safe tar archive and never runs commands from its manifest.
"""

import argparse
import errno
import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path


MAX_ARCHIVE_BYTES = 256 * 1024 * 1024
MAX_RELEASE_BYTES = 512 * 1024 * 1024
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_METADATA_BYTES = 1024 * 1024
MAX_FILES = 256
DISK_RESERVE_BYTES = 16 * 1024 * 1024
STOP_TIMEOUT_SECONDS = 45
HEALTH_TIMEOUT_SECONDS = 30
HEALTH_POLL_SECONDS = 0.25
SERVICE_NAME = "skittlesorter"
RUNTIMES = {"linux-arm", "linux-arm64"}
VERSION_PATTERN = re.compile(r"[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}\Z")


def _is_external_or_secret_file(filename):
    name = filename.lower()
    return (
        name.startswith("appsettings")
        or name in {"config", "config.json", "credentials", "credentials.json"}
        or name.endswith((".pem", ".key", ".pfx", ".p12", ".crt", ".cer", ".secret"))
    )

try:
    import fcntl as _fcntl
except ImportError:  # pragma: no cover - used only on Windows for local tests
    _fcntl = None


class InstallError(RuntimeError):
    pass


class LockBusy(RuntimeError):
    pass


def _fsync_directory(directory):
    if os.name == "nt":
        return
    fd = os.open(str(directory), os.O_RDONLY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def _atomic_json(path, value):
    path = Path(path)
    fd, temporary = tempfile.mkstemp(prefix=".journal-", dir=str(path.parent))
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        _fsync_directory(path.parent)
    finally:
        try:
            os.unlink(temporary)
        except FileNotFoundError:
            pass


def _atomic_symlink(target, link):
    """Replace a link atomically without exposing a missing-current interval."""
    link = Path(link)
    temporary = link.with_name(f".{link.name}.{os.getpid()}.new")
    try:
        temporary.unlink()
    except FileNotFoundError:
        pass
    if os.name == "nt":
        target = target.replace("/", os.sep)
    os.symlink(target, temporary, target_is_directory=True)
    if os.name == "nt" and (link.exists() or link.is_symlink()):
        # Windows is supported for unit-test execution only; Linux uses the atomic replace below.
        link.unlink()
    os.replace(temporary, link)
    _fsync_directory(link.parent)


def _read_link_version(link, releases):
    link = Path(link)
    try:
        if not link.is_symlink():
            return None
        target = os.readlink(link)
        relative = Path(target)
        if relative.is_absolute() or len(relative.parts) != 2 or relative.parts[0] != "releases":
            raise InstallError(f"Unexpected release link target: {link}")
        candidate = (link.parent / target).resolve(strict=True)
        release_root = Path(releases).resolve(strict=True)
        if (
            candidate.parent != release_root
            or candidate.name != relative.parts[1]
            or not candidate.is_dir()
            or (release_root / relative.parts[1]).is_symlink()
        ):
            raise InstallError(f"Unexpected release link target: {link}")
        return candidate.name
    except FileNotFoundError:
        return None


class InstallLock:
    """An OS advisory lock, automatically released if the installer dies."""

    def __init__(self, path, blocking=True):
        self.path = Path(path)
        self.blocking = blocking
        self.stream = None

    def __enter__(self):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.stream = open(self.path, "a+b")
        try:
            if _fcntl is not None:
                flags = _fcntl.LOCK_EX if self.blocking else _fcntl.LOCK_EX | _fcntl.LOCK_NB
                _fcntl.flock(self.stream.fileno(), flags)
            else:  # pragma: no cover - Windows test runner
                import msvcrt

                self.stream.seek(0)
                if self.stream.read(1) == b"":
                    self.stream.write(b"\0")
                    self.stream.flush()
                self.stream.seek(0)
                mode = msvcrt.LK_LOCK if self.blocking else msvcrt.LK_NBLCK
                msvcrt.locking(self.stream.fileno(), mode, 1)
        except OSError as exc:
            self.stream.close()
            self.stream = None
            if not self.blocking and exc.errno in {errno.EACCES, errno.EAGAIN}:
                raise LockBusy from exc
            raise
        return self

    def __exit__(self, *_):
        if self.stream is not None:
            if _fcntl is not None:
                _fcntl.flock(self.stream.fileno(), _fcntl.LOCK_UN)
            else:  # pragma: no cover - Windows test runner
                import msvcrt

                self.stream.seek(0)
                msvcrt.locking(self.stream.fileno(), msvcrt.LK_UNLCK, 1)
            self.stream.close()


class ServiceManager:
    def _run(self, *args, timeout=None):
        try:
            return subprocess.run(
                ["systemctl", *args],
                check=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                timeout=timeout,
            )
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as exc:
            detail = getattr(exc, "stderr", None) or str(exc)
            raise InstallError(f"systemctl {' '.join(args)} failed: {detail}") from exc

    def stop(self):
        # A timeout is a hard failure; systemd is configured not to SIGKILL the sorter.
        self._run("stop", SERVICE_NAME, timeout=STOP_TIMEOUT_SECONDS)

    def start(self):
        self._run("start", SERVICE_NAME, timeout=STOP_TIMEOUT_SECONDS)

    def is_active(self):
        return subprocess.run(
            ["systemctl", "is-active", "--quiet", SERVICE_NAME],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=STOP_TIMEOUT_SECONDS,
        ).returncode == 0

    def main_pid(self):
        result = self._run("show", "--property=MainPID", "--value", SERVICE_NAME)
        try:
            return int(result.stdout.strip())
        except ValueError:
            return 0

    def process_matches_release(self, pid, executable):
        if pid <= 1:
            return False
        try:
            if not Path(f"/proc/{pid}").exists():
                return False
            process_exe = Path(f"/proc/{pid}/exe").resolve(strict=True)
            return process_exe == Path(executable).resolve(strict=True)
        except (OSError, RuntimeError):
            return False


class Installer:
    def __init__(self, root, ready_file, service=None):
        self.root = Path(root)
        self.releases = self.root / "releases"
        self.current = self.root / "current"
        self.last_good = self.root / "last-good"
        self.journal = self.root / ".installer-journal.json"
        self.lock_path = self.root / ".installer.lock"
        self.ready_file = Path(ready_file)
        self.service = service or ServiceManager()

    def install(self, payload, version, runtime):
        self._validate_arguments(payload, version, runtime)
        if not self.root.is_absolute() or self.root.is_symlink():
            raise InstallError("Installer root must be an absolute, non-symlink path")
        self.root.mkdir(parents=True, exist_ok=True, mode=0o755)
        if self.root.is_symlink():
            raise InstallError("Installer root must not be a symlink")
        self.releases.mkdir(parents=True, exist_ok=True, mode=0o755)
        if self.releases.is_symlink():
            raise InstallError("Installer root and releases directory must not be symlinks")
        os.chmod(self.root, 0o755)
        os.chmod(self.releases, 0o755)
        with InstallLock(self.lock_path):
            self.recover()
            return self._install_locked(Path(payload), version, runtime)

    def recover_at_boot(self):
        if not self.root.is_dir() or self.root.is_symlink() or not self.releases.is_dir() or self.releases.is_symlink():
            raise InstallError("Installer root and releases directory must be initialized before boot recovery")
        try:
            lock = InstallLock(self.lock_path, blocking=False)
            with lock:
                try:
                    self.ready_file.unlink()
                except FileNotFoundError:
                    pass
                self.recover(boot_mode=True)
        except LockBusy:
            # The installer starts the service while holding this same lock; this is not boot recovery.
            return False
        return True

    def _validate_arguments(self, payload, version, runtime):
        if not VERSION_PATTERN.fullmatch(version) or version in {".", ".."}:
            raise InstallError("Invalid version string")
        if runtime not in RUNTIMES:
            raise InstallError(f"Unsupported runtime: {runtime}")
        payload = Path(payload)
        if not payload.is_file():
            raise InstallError("Payload archive does not exist")
        if payload.stat().st_size > MAX_ARCHIVE_BYTES:
            raise InstallError("Payload archive exceeds the compressed size limit")

    def _extract_payload(self, archive, destination, expected_version, expected_runtime):
        seen = set()
        total_size = 0
        file_count = 0
        entry_count = 0
        metadata = None
        executable_marked = False
        try:
            with open(archive, "rb") as source, tarfile.open(fileobj=source, mode="r|gz") as tar:
                for member in tar:
                    entry_count += 1
                    if entry_count > MAX_FILES:
                        raise InstallError("Payload contains too many archive entries")
                    name = member.name
                    if "\\" in name or name.startswith("/") or re.match(r"^[A-Za-z]:", name):
                        raise InstallError(f"Unsafe archive path: {name!r}")
                    parts = [part for part in name.split("/") if part not in ("", ".")]
                    if any(part == ".." for part in parts) or len(parts) > 1:
                        raise InstallError(f"Payload must contain only flat files: {name!r}")
                    normalized = parts[0] if parts else "."
                    if normalized in seen:
                        raise InstallError(f"Duplicate archive path: {normalized}")
                    seen.add(normalized)
                    if not parts:
                        if member.isdir():
                            continue
                        raise InstallError(f"Unsafe archive entry: {name!r}")
                    filename = parts[0]
                    if member.isdir():
                        continue
                    if not member.isfile() or member.issym() or member.islnk():
                        raise InstallError(f"Unsupported archive entry type: {filename}")
                    if _is_external_or_secret_file(filename):
                        raise InstallError(f"External configuration/secret file is forbidden: {filename}")
                    if member.size < 0 or member.size > MAX_FILE_BYTES:
                        raise InstallError(f"Invalid or oversized archive member: {filename}")
                    if filename == "release.json" and member.size > MAX_METADATA_BYTES:
                        raise InstallError("release.json exceeds the metadata size limit")
                    file_count += 1
                    total_size += member.size
                    if file_count > MAX_FILES or total_size > MAX_RELEASE_BYTES:
                        raise InstallError("Payload exceeds file-count or expanded-size limit")
                    output = destination / filename
                    source_file = tar.extractfile(member)
                    if source_file is None:
                        raise InstallError(f"Unable to read archive member: {filename}")
                    written = 0
                    with source_file, open(output, "xb") as target:
                        while True:
                            chunk = source_file.read(1024 * 1024)
                            if not chunk:
                                break
                            written += len(chunk)
                            if written > member.size or written > MAX_FILE_BYTES:
                                raise InstallError(f"Archive member expanded beyond its declared size: {filename}")
                            target.write(chunk)
                        if written != member.size:
                            raise InstallError(f"Archive member size mismatch: {filename}")
                    os.chmod(output, 0o444 | (member.mode & 0o111))
                    if filename == "skittlesorter":
                        executable_marked = bool(member.mode & 0o111)
                    if filename == "release.json":
                        try:
                            metadata = json.loads(output.read_text(encoding="utf-8"))
                        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
                            raise InstallError("Invalid release.json") from exc
            if "skittlesorter" not in seen or "release.json" not in seen:
                raise InstallError("Payload must include skittlesorter and release.json")
            executable = destination / "skittlesorter"
            if (
                not executable.is_file()
                or not executable_marked
                or (os.name != "nt" and not (executable.stat().st_mode & 0o111))
            ):
                raise InstallError("skittlesorter must be an executable regular file")
            if (
                not isinstance(metadata, dict)
                or metadata.get("version") != expected_version
                or metadata.get("runtime") != expected_runtime
                or not isinstance(metadata.get("features"), list)
                or any(not isinstance(feature, str) or not feature for feature in metadata["features"])
            ):
                raise InstallError("release.json version/runtime/features do not match the requested release")
            return total_size
        except (tarfile.TarError, EOFError, OSError) as exc:
            if isinstance(exc, InstallError):
                raise
            raise InstallError(f"Invalid payload archive: {exc}") from exc

    def _install_locked(self, payload, version, runtime):
        target = self.releases / version
        if target.exists() or target.is_symlink():
            raise InstallError(f"Release already exists and is immutable: {version}")
        previous = _read_link_version(self.current, self.releases)
        free = shutil.disk_usage(self.root).free
        if free < DISK_RESERVE_BYTES:
            raise InstallError("Insufficient free space for installation reserve")
        staging = Path(tempfile.mkdtemp(prefix=".staging-", dir=str(self.releases)))
        self._write_journal(
            {
                "phase": "staging",
                "target": version,
                "previous": previous,
                "staging": staging.name,
            }
        )
        try:
            expanded = self._extract_payload(payload, staging, version, runtime)
            if shutil.disk_usage(self.root).free < DISK_RESERVE_BYTES:
                raise InstallError("Insufficient free space after staging payload")
            # Tar sizes are checked during extraction; this second check includes filesystem overhead reserve.
            if expanded + DISK_RESERVE_BYTES > free:
                raise InstallError("Insufficient disk space for payload and reserve")
            os.chmod(staging, 0o555)
            os.rename(staging, target)
            _fsync_directory(self.releases)
            self._write_journal(
                {"phase": "staged", "target": version, "previous": previous, "started_at": None}
            )
        except Exception:
            shutil.rmtree(staging, ignore_errors=True)
            if target.exists() and _read_link_version(self.current, self.releases) != version:
                shutil.rmtree(target, ignore_errors=True)
            self.journal.unlink(missing_ok=True)
            raise

        try:
            self.service.stop()
            self._write_journal(
                {"phase": "stopped", "target": version, "previous": previous, "started_at": None}
            )
            started_at = time.time()
            self._write_journal(
                {"phase": "activated", "target": version, "previous": previous, "started_at": started_at}
            )
            _atomic_symlink(f"releases/{version}", self.current)
            self._start_and_verify(version, target, started_at)
            self._write_journal(
                {
                    "phase": "health_verified",
                    "target": version,
                    "previous": previous,
                    "started_at": started_at,
                }
            )
            self._finalize_commit(version, previous)
            return version
        except Exception as exc:
            try:
                self._rollback(version, previous)
            except Exception as recovery_error:
                raise InstallError(
                    f"Installation failed ({exc}); rollback also failed ({recovery_error}); "
                    f"journal retained at {self.journal}"
                ) from recovery_error
            if isinstance(exc, InstallError):
                raise
            raise InstallError(f"Installation failed: {exc}") from exc

    def _wait_until_ready(self, version, release, started_at):
        deadline = time.monotonic() + HEALTH_TIMEOUT_SECONDS
        ready = self.ready_file
        while time.monotonic() < deadline:
            try:
                data = json.loads(ready.read_text(encoding="utf-8"))
                pid = data.get("processId") if isinstance(data, dict) else None
                ready_at = datetime.fromisoformat(data["readyAt"].replace("Z", "+00:00"))
                if ready_at.tzinfo is None or ready_at.utcoffset() != timedelta(0):
                    raise ValueError("readyAt must be UTC")
                ready_epoch = ready_at.astimezone(timezone.utc).timestamp()
                main_pid = self.service.main_pid()
                if (
                    data.get("version") == version
                    and isinstance(pid, int)
                    and pid > 1
                    and pid == main_pid
                    and ready_epoch + 1.0 >= started_at
                    and self.service.process_matches_release(pid, release / "skittlesorter")
                ):
                    return
            except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError, AttributeError):
                pass
            time.sleep(HEALTH_POLL_SECONDS)
        raise InstallError(f"Service did not report fresh readiness for version {version}")

    def _start_and_verify(self, version, release, started_at=None):
        # A stale marker must never make a newly started but unhealthy process look ready.
        try:
            self.ready_file.unlink()
        except FileNotFoundError:
            pass
        started_at = time.time() if started_at is None else started_at
        self.service.start()
        self._wait_until_ready(version, release, started_at)
        return started_at

    def _write_journal(self, value):
        _atomic_json(self.journal, value)

    def _finalize_commit(self, target, previous):
        # LKG is changed only after readiness; a crash before this point remains recoverable.
        if _read_link_version(self.current, self.releases) != target:
            raise InstallError("Cannot commit because current does not point to the verified release")
        _atomic_symlink(f"releases/{target}", self.last_good)
        self.journal.unlink(missing_ok=True)
        _fsync_directory(self.root)

    def _validate_existing_release(self, version):
        release = self.releases / version
        executable = release / "skittlesorter"
        manifest = release / "release.json"
        if (
            release.is_symlink()
            or not release.is_dir()
            or executable.is_symlink()
            or not executable.is_file()
            or (os.name != "nt" and not executable.stat().st_mode & 0o111)
            or not manifest.is_file()
        ):
            raise InstallError(f"Cannot restore incomplete release {version}")
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise InstallError(f"Cannot restore release {version}: invalid release.json") from exc
        if not isinstance(data, dict) or data.get("version") != version or data.get("runtime") not in RUNTIMES:
            raise InstallError(f"Cannot restore release {version}: release metadata mismatch")

    def _remove_failed_release(self, target):
        if (
            _read_link_version(self.current, self.releases) != target
            and _read_link_version(self.last_good, self.releases) != target
        ):
            shutil.rmtree(self.releases / target, ignore_errors=True)
            _fsync_directory(self.releases)

    def _rollback(self, target, previous):
        self.service.stop()
        if previous:
            self._validate_existing_release(previous)
            _atomic_symlink(f"releases/{previous}", self.current)
            _atomic_symlink(f"releases/{previous}", self.last_good)
            self._start_and_verify(previous, self.releases / previous)
        else:
            try:
                self.current.unlink()
            except FileNotFoundError:
                pass
            _fsync_directory(self.root)
        self._remove_failed_release(target)
        self.journal.unlink(missing_ok=True)
        _fsync_directory(self.root)

    def recover(self, boot_mode=False):
        if not self.journal.exists():
            return
        try:
            transaction = json.loads(self.journal.read_text(encoding="utf-8"))
            if not isinstance(transaction, dict):
                raise ValueError("journal must contain an object")
            phase = transaction["phase"]
            target = transaction["target"]
            previous = transaction.get("previous")
            if (
                not isinstance(phase, str)
                or not isinstance(target, str)
                or not VERSION_PATTERN.fullmatch(target)
                or (previous is not None and (not isinstance(previous, str) or not VERSION_PATTERN.fullmatch(previous)))
            ):
                raise ValueError("invalid version in journal")
        except (OSError, TypeError, ValueError, KeyError, json.JSONDecodeError) as exc:
            raise InstallError(f"Cannot safely recover installer journal: {exc}") from exc

        if phase == "health_verified":
            # The health check is durable evidence that the new release was running.
            self._finalize_commit(target, previous)
            return
        if boot_mode:
            if phase == "staging":
                staging_name = transaction.get("staging")
                if (
                    not isinstance(staging_name, str)
                    or not staging_name.startswith(".staging-")
                    or "/" in staging_name
                ):
                    raise InstallError("Cannot safely recover installer staging path")
                shutil.rmtree(self.releases / staging_name, ignore_errors=True)
                self._remove_failed_release(target)
            elif phase in {"staged", "stopped"}:
                if previous:
                    self._validate_existing_release(previous)
                    if _read_link_version(self.current, self.releases) != previous:
                        raise InstallError("Unexpected active release while recovering a staged install")
                elif _read_link_version(self.current, self.releases) is not None:
                    raise InstallError("Unexpected active release while recovering a first install")
                self._remove_failed_release(target)
            elif phase == "activated":
                if previous:
                    self._validate_existing_release(previous)
                    _atomic_symlink(f"releases/{previous}", self.current)
                    _atomic_symlink(f"releases/{previous}", self.last_good)
                else:
                    self.current.unlink(missing_ok=True)
                self._remove_failed_release(target)
            else:
                raise InstallError(f"Unknown installer journal phase: {phase}")
            self.journal.unlink(missing_ok=True)
            _fsync_directory(self.root)
            return
        if phase == "staging":
            staging_name = transaction.get("staging")
            if not isinstance(staging_name, str) or not staging_name.startswith(".staging-") or "/" in staging_name:
                raise InstallError("Cannot safely recover installer staging path")
            shutil.rmtree(self.releases / staging_name, ignore_errors=True)
            if _read_link_version(self.current, self.releases) != target and _read_link_version(
                self.last_good, self.releases
            ) != target:
                shutil.rmtree(self.releases / target, ignore_errors=True)
            self.journal.unlink(missing_ok=True)
            _fsync_directory(self.root)
            return
        if phase in {"staged", "stopped"}:
            if previous:
                if _read_link_version(self.current, self.releases) != previous:
                    raise InstallError("Unexpected active release while recovering a staged install")
                if not self.service.is_active():
                    self._start_and_verify(previous, self.releases / previous)
            elif _read_link_version(self.current, self.releases) is not None:
                raise InstallError("Unexpected active release while recovering a first install")
            self._remove_failed_release(target)
            self.journal.unlink(missing_ok=True)
            _fsync_directory(self.root)
            return
        if phase == "activated":
            self._rollback(target, previous)
            return
        raise InstallError(f"Unknown installer journal phase: {phase}")


def main(argv=None):
    parser = argparse.ArgumentParser(description="Install a verified Skittle Sorter release payload")
    parser.add_argument("command", choices=["install", "recover"])
    parser.add_argument("--root", required=True, help="Release root on the same filesystem as current")
    parser.add_argument("--payload", help="Verified flat release tar.gz")
    parser.add_argument("--version", help="Expected release version")
    parser.add_argument("--runtime", choices=sorted(RUNTIMES))
    parser.add_argument("--ready-file", default="/var/lib/skittlesorter/ready.json")
    args = parser.parse_args(argv)
    try:
        installer = Installer(args.root, args.ready_file)
        if args.command == "recover":
            recovered = installer.recover_at_boot()
            print("Installer recovery complete" if recovered else "Installer active; startup recovery deferred")
            return 0
        if not all((args.payload, args.version, args.runtime)):
            parser.error("install requires --payload, --version, and --runtime")
        version = installer.install(args.payload, args.version, args.runtime)
        print(f"Installed Skittle Sorter {version}")
        return 0
    except (InstallError, OSError) as exc:
        print(f"Installation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
