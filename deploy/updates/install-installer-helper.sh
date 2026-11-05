#!/bin/sh
set -eu

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this setup script as root." >&2
    exit 1
fi

HELPER_SOURCE=${1:-/usr/lib/skittlesorter/installer-helper}
HELPER_DEST=/usr/lib/skittlesorter/installer-helper
SUDOERS_DEST=/etc/sudoers.d/skittlesorter-installer
DOWNLOAD_DIR=/var/cache/skittlesorter-updates/incoming
SNAPSHOT_DIR=/var/lib/skittlesorter-installer/payloads

if ! id skittlesorter >/dev/null 2>&1; then
    echo "The skittlesorter service account must exist before installing the helper." >&2
    exit 1
fi
if [ ! -f "$HELPER_SOURCE" ] || [ -L "$HELPER_SOURCE" ]; then
    echo "Trusted helper source is missing or is a symlink: $HELPER_SOURCE" >&2
    exit 1
fi
if [ ! -f /usr/lib/skittlesorter/installer.py ] || [ -L /usr/lib/skittlesorter/installer.py ]; then
    echo "Install the shared installer at /usr/lib/skittlesorter/installer.py first." >&2
    exit 1
fi
if [ ! -x /usr/bin/python3 ]; then
    echo "/usr/bin/python3 is required." >&2
    exit 1
fi

install -d -o skittlesorter -g skittlesorter -m 0700 /var/lib/skittlesorter
install -d -o skittlesorter -g skittlesorter -m 0700 /var/cache/skittlesorter-updates "$DOWNLOAD_DIR"
chown skittlesorter:skittlesorter /var/cache/skittlesorter-updates "$DOWNLOAD_DIR"
chmod 0700 /var/cache/skittlesorter-updates "$DOWNLOAD_DIR"
install -d -o root -g root -m 0700 /var/lib/skittlesorter-installer "$SNAPSHOT_DIR"
chown root:root /var/lib/skittlesorter-installer "$SNAPSHOT_DIR"
chmod 0700 /var/lib/skittlesorter-installer "$SNAPSHOT_DIR"

install -d -o root -g root -m 0755 /usr/lib/skittlesorter
if [ -L /usr/lib/skittlesorter ] || [ -L "$HELPER_DEST" ]; then
    echo "Refusing to install through a symlinked library path." >&2
    exit 1
fi
if [ "$HELPER_SOURCE" != "$HELPER_DEST" ]; then
    install -o root -g root -m 0755 "$HELPER_SOURCE" "$HELPER_DEST"
else
    chown root:root "$HELPER_DEST"
    chmod 0755 "$HELPER_DEST"
fi
if [ -L "$SUDOERS_DEST" ]; then
    echo "Refusing to overwrite a symlinked sudoers entry." >&2
    exit 1
fi
SUDOERS_TEMP=$(mktemp /etc/sudoers.d/.skittlesorter-installer.XXXXXX)
trap 'rm -f "$SUDOERS_TEMP"' EXIT HUP INT TERM
printf '%s\n' 'skittlesorter ALL=(root) NOPASSWD: /usr/lib/skittlesorter/installer-helper' \
    > "$SUDOERS_TEMP"
chown root:root "$SUDOERS_TEMP"
chmod 0440 "$SUDOERS_TEMP"
if ! visudo -cf "$SUDOERS_TEMP"; then
    echo "Rejected invalid sudoers entry." >&2
    exit 1
fi
install -o root -g root -m 0440 "$SUDOERS_TEMP" "$SUDOERS_DEST"
rm -f "$SUDOERS_TEMP"
trap - EXIT HUP INT TERM

echo "Installed restricted updater privilege helper: $HELPER_DEST"
