#!/usr/bin/python3
"""GA microsoft/script:1 adapter; installation is delegated to the fixed local installer."""
import argparse
import json
import subprocess
import sys
from pathlib import Path


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    for name in ("download", "install", "apply", "cancel", "is-installed"):
        action.add_argument("--action-" + name, action="store_true")
    parser.add_argument("--payload", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--runtime", choices=["linux-arm", "linux-arm64"], required=True)
    parser.add_argument("--work-folder", type=Path, required=True)
    parser.add_argument("--result-file", type=Path, required=True)
    parser.add_argument("--installed-criteria", required=True)
    args = parser.parse_args(argv)
    result = {"resultCode": 0, "extendedResultCode": 1, "resultDetails": ""}
    exit_code = 0
    try:
        if args.installed_criteria != args.version or Path(args.payload).name != args.payload:
            raise ValueError("Unexpected installed criteria or payload path.")
        if args.action_is_installed or args.action_apply:
            installed = json.loads(Path("/opt/skittlesorter/current/release.json").read_text())
            matches = installed["version"] == args.version and installed["runtime"] == args.runtime
            if args.action_is_installed:
                result["resultCode"] = 900 if matches else 901
            elif matches:
                result["resultCode"] = 700
            else:
                raise ValueError("Applied release does not match the requested version.")
        elif args.action_install:
            subprocess.run(
                ["/usr/bin/python3", "/usr/lib/skittlesorter/installer.py", "install",
                 "--root", "/opt/skittlesorter", "--payload", str(args.work_folder / args.payload),
                 "--version", args.version, "--runtime", args.runtime], check=True)
            result["resultCode"] = 600
        elif args.action_download:
            if not (args.work_folder / args.payload).is_file():
                raise ValueError("The agent has not downloaded the application payload.")
            result["resultCode"] = 500
        elif args.action_cancel:
            # Each invocation is synchronous; never pretend to stop another process mid-install.
            if Path("/opt/skittlesorter/.installer-journal.json").exists():
                raise RuntimeError("An installation transaction requires operator recovery.")
            result["resultCode"] = 800
        result["extendedResultCode"] = 0
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
        if args.action_is_installed and isinstance(error, FileNotFoundError):
            result.update(resultCode=901, extendedResultCode=0)
        else:
            result["resultDetails"] = str(error)
            print(str(error), file=sys.stderr)
            exit_code = 1
    args.result_file.write_text(json.dumps(result), encoding="utf-8")
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
