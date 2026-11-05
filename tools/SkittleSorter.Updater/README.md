# Skittle Sorter ADR preview updater host

Unofficial .NET 10 **regular registered-device** Software Updates preview client. It talks only to DPS using X.509 and API `2026-11-02-preview`; it does not register a new device, open an IoT Hub connection, or require ARM credentials. Microsoft .NET Software Updates support is not assumed. See [protocol evidence](../../AzureSoftwareUpdatesFramework/PROTOCOL-EVIDENCE.md).

## CLI and configuration

```sh
dotnet SkittleSorter.Updater.dll --config /etc/skittlesorter/software-updates.json
# Explicit manual check rather than shortening the normal polling interval:
dotnet SkittleSorter.Updater.dll --config /etc/skittlesorter/software-updates.json --check-once
```

From the repository on Windows:

```powershell
dotnet run --project tools\SkittleSorter.Updater -- --config C:\device-config\software-updates.json --check-once
```

The actual installer helper is Linux-only; Windows is for building/unit testing the client. A safe configuration example (replace placeholders with independently verified operator data):

```json
{
  "idScope": "<DPS ID scope>",
  "registrationId": "<already registered X.509 device ID>",
  "globalEndpoint": "global.azure-devices-provisioning.net",
  "certificatePath": "/etc/skittlesorter/device-cert.pem",
  "privateKeyPath": "/etc/skittlesorter/device-key.pem",
  "statePath": "/var/lib/skittlesorter/software-updates-state.json",
  "provisioningLockPath": "/var/lib/skittlesorter/provisioning.lock",
  "downloadDirectory": "/var/cache/skittlesorter-updates/incoming",
  "runtime": "linux-arm64",
  "pollIntervalHours": 4,
  "maximumPayloadBytes": 268435456,
  "requestTimeoutSeconds": 60,
  "agentInfo": {
    "agentSdkVersion": "skittlesorter-preview-1",
    "agentProfile": "<service-registered capability profile for this handler>",
    "compatibilityProperties": {
      "manufacturer": "SkittleSorter",
      "model": "Pi4",
      "architecture": "linux-arm64"
    }
  },
  "trustedRootKeys": [
    {
      "kid": "<independently verified root key ID>",
      "modulus": "<base64 or base64url RSA modulus>",
      "exponent": "AQAB",
      "disabled": false
    }
  ]
}
```

Use the detected Pi userland runtime (`linux-arm` or `linux-arm64`) consistently in configuration, compatibility properties, and package. All state/lock/download paths must be absolute. Polling must be at least four hours; compatibility properties must have 1–5 entries. Root keys are public keys, but configuration and key files must not be writable by update payloads. Do not copy root keys from a service response and assume they are trusted.

## Startup, privileges, and DPS coordination

1. Register/provision the device with supported X.509 credentials first. Install trusted server CAs in the host trust store.
2. Install the parent-owned Python installer and root-owned helper/bootstrap from `deploy/updates`. The updater service runs as the `skittlesorter` account, not root. Install the narrowly scoped sudoers entry before enabling it.
3. Give that account read access to its config/certificate/private key and write access to the state directory, shared lock, and fixed incoming directory. Protect `/usr/lib/skittlesorter/installer-helper`, the Python installer, and sudoers against modification by that account.
4. Configure sorter provisioning to use **the exact same** `provisioningLockPath` with exclusive `FileStream`/`FileShare.None` locking. Each check/report acquires it before connecting DPS, disconnects/disposes the session, then releases it. The updater does not hold a DPS session or this lock during payload download, installation, or polling sleep, allowing sorter restart/reprovisioning. Sharing a device identity without this coordination is not supported.
5. Start with a supervised, operator-observed `--check-once` run before enabling the long-running service. Startup replays owed reports before asking for another update, except unresolved installer-boundary state stops startup for reconciliation.

Verified archives are downloaded to `/var/cache/skittlesorter-updates/incoming/` and passed by basename only, without a shell:

```text
/usr/bin/sudo -n /usr/lib/skittlesorter/installer-helper install --payload <basename.tar.gz> --version <version> --runtime <linux-arm|linux-arm64>
```

These executable paths are fixed, not selected by config or manifest. The helper validates/snapshots into `/var/lib/skittlesorter-installer/payloads/` and calls the fixed Python installer with root `/opt/skittlesorter`. It owns service stop, activation, readiness, and rollback. Nonzero exit is an installation failure; no password prompt is allowed. The compressed archive limit is 256 MiB.

## Exact supported device manifest subset

- `manifestVersion` is the string `"5"` (not an import-manifest `"5.0"`).
- `updateId` contains `provider`, `name`, and `version`.
- `instructions` contains only `steps`, with exactly one step.
- The step contains only `handler`, `files`, and `handlerProperties`; `handler` is exactly `skittlesorter/application:1`.
- `handlerProperties` contains exactly `installedCriteria` equal to `updateId.version`, and `runtime` equal to the configured Pi runtime.
- The step's single `files` value identifies the only entry in the manifest `files` object and in service `fileUrls`. File metadata contains only `fileName` (safe `.tar.gz` basename), positive `sizeInBytes`, and `hashes` containing only base64 `sha256`.
- Both nested RS256 signatures and the signed hash of the exact unescaped manifest string must verify before any payload download. Every payload URL/redirect must use HTTPS and downloaded bytes must match signed size/SHA-256.

An import manifest is not a device manifest. Converting its file list to a keyed service map and `filename` to `fileName` is necessary but may not be sufficient: this client rejects import `"5.0"` and import step `type`/`description` fields. The service must produce its actual signed device manifest; locally editing a signed manifest invalidates its signature. Reference, multistep, unknown-handler, and arbitrary command execution are unsupported.

## Limitations and live integration gates

- Azure/DPS preview interoperability, actual service capability profile/device-manifest conversion, and Pi/helper/systemd coexistence remain unverified. Local tests/builds are not live OTA acceptance.
- Each check/report opens a short-lived DPS session under the shared lock. Lost sessions are disposed and recreated with at most four attempts and 1/2/4-second waits per operation. The same owed report is replayed after reconnect; it is cleared only after acknowledgement. After transport recovery is exhausted, a daemon waits until its next normal poll to try again; a one-shot invocation exits with the report still stored.
- Complete machine-code error recovery and report-conflict corrective polling remain incomplete. A 409 (or other permanent report rejection) is surfaced and stops the host with its report/state preserved for operator reconciliation; it is never acknowledged as success or automatically polled past.
- Failed workflow waits are durably recorded with one-hour doubling backoff capped at 24 hours plus jitter. The host uses its own JSON state format, not the C SDK private blob.
- If interrupted at `Installing`, the host refuses to guess the outcome or reinstall. Reconcile `/opt/skittlesorter/.installer-journal.json`, active release, and updater state under operator supervision first.
- Root rotation/revocation is operator-managed through independently trusted config and restart. Service root-key URLs do not add trust; dynamic signed root-package rotation and signing-key revocation packages are not implemented.
- `--check-once` does not lower normal polling, but transient service responses are retried; its whole-operation retry deadline is not yet bounded. Downloads also lack an independent elapsed-time limit.
- Terminal results depend on the helper's actual exit result and health/rollback guarantees. Privileged helper installation and sudo authorization are deployment prerequisites, not granted by this host.
