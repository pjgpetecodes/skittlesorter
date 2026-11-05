#!/bin/bash
set -euo pipefail

if [[ ! -r /etc/os-release ]] || ! command -v dpkg >/dev/null; then
    echo "Expected a Debian-family Raspberry Pi OS installation." >&2
    exit 1
fi
cat /etc/os-release
architecture="$(dpkg --print-architecture)"
bits="$(getconf LONG_BIT)"
case "$architecture:$bits" in
    arm64:64) runtime=linux-arm64 ;;
    armhf:32) runtime=linux-arm ;;
    *) echo "Unsupported userland: $architecture ($bits-bit). Kernel: $(uname -m)" >&2; exit 1 ;;
esac
printf 'Userland: %s (%s-bit)\nApplication runtime: %s\n' "$architecture" "$bits" "$runtime"
python3 --version
systemctl --version
df -h /opt /var/lib
echo "Check this exact OS against .NET 10 and the pinned Device Update agent package before installation."
if systemctl is-active --quiet deviceupdate-agent && systemctl is-active --quiet skittlesorter-updater; then
    echo "Both cloud update controllers are active. Stop one before proceeding." >&2
    exit 1
fi
echo "Preflight is informational; no packages, service settings, credentials or Azure resources were changed."
