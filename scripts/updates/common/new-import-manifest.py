#!/usr/bin/env python3
"""Generate channel-specific schema-5 import manifests from an existing app payload."""
import argparse
import base64
import hashlib
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path


def generate(payload, version, runtime, channel, output):
    if output.exists():
        raise FileExistsError(output)
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Use a three-part numeric version.")
    if runtime not in ("linux-arm", "linux-arm64") or channel not in ("ga", "adr"):
        raise ValueError("Invalid runtime/channel.")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,254}", payload.name):
        raise ValueError("Payload filenames must be simple basenames without spaces or shell metacharacters.")
    def metadata(path):
        digest = hashlib.sha256()
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        return {"filename": path.name, "sizeInBytes": path.stat().st_size,
                "hashes": {"sha256": base64.b64encode(digest.digest()).decode()}}

    files = [metadata(payload)]
    step = {"type": "inline", "description": "Install and health-check the Skittle Sorter release",
            "handler": "skittlesorter/application:1", "files": [payload.name],
            "handlerProperties": {"installedCriteria": version, "runtime": runtime}}
    if channel == "ga":
        adapter = Path(__file__).parent.parent / "device-update" / "install-sorter.py"
        destination = output.parent / adapter.name
        output.parent.mkdir(parents=True, exist_ok=True)
        if destination.exists():
            if destination.read_bytes() != adapter.read_bytes():
                raise FileExistsError("Use a fresh output directory for a changed GA adapter.")
        else:
            shutil.copyfile(adapter, destination)
        files.append(metadata(destination))
        step["handler"] = "microsoft/script:1"
        step["files"].append(destination.name)
        step["handlerProperties"] = {
            "installedCriteria": version, "scriptFileName": destination.name,
            "arguments": f"--payload {payload.name} --version {version} --runtime {runtime}"}
    manifest = {
        "manifestVersion": "5.0",
        "updateId": {"provider": "SkittleSorter", "name": f"sorter-{runtime}", "version": version},
        "compatibility": [{"manufacturer": "SkittleSorter", "model": "Pi4", "architecture": runtime}],
        "instructions": {"steps": [step]}, "files": files,
        "createdDateTime": datetime.now(timezone.utc).isoformat(),
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as stream:
        json.dump(manifest, stream, indent=2)
    return manifest


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--channel", choices=["ga", "adr"], required=True)
    args = parser.parse_args()
    generate(args.payload, args.version, args.runtime, args.channel, args.output)
