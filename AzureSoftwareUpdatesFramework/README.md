# Isolated ADR Software Updates preview client

This is an unofficial .NET 10 client for the **regular, already-registered DPS device** route in the published `2026-11-02-preview` contract. It is separate from `AzureDpsFramework`, pins MQTTnet 4.3.3.952, and opens a DPS MQTT session only: it does not connect to IoT Hub or require Azure ARM credentials.

This is a constrained demonstration client, not a Microsoft-supported agent or production fleet updater. Microsoft's pinned .NET SDK capability matrix says DPS Software Updates are unsupported. The isolated implementation is based on the published C SDK protocol evidence and does not claim compatibility with a future Microsoft .NET API.

## Configuration and run

Create a config file outside the release directory (for example `/etc/skittlesorter/software-updates.json`) and protect the private key and state directories using the device's normal file permissions. A shortened example:

```json
{
  "idScope": "<DPS ID scope>",
  "registrationId": "<registered device ID>",
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
    "agentProfile": "skittlesorter/application:1",
    "compatibilityProperties": {
      "manufacturer": "SkittleSorter",
      "model": "Pi4"
    }
  },
  "trustedRootKeys": [
    {
      "kid": "<operator-verified root key ID>",
      "modulus": "<base64url RSA modulus>",
      "exponent": "AQAB",
      "disabled": false
    }
  ]
}
```

`trustedRootKeys` is mandatory and must be established independently by the operator. Values are public RSA key components in base64/base64url form. Do not copy a key or URL from a DPS response and treat it as trusted. Keep the configured root baseline outside the update payload. Multiple roots and `disabled` revocations are supported; rotation requires an operator-reviewed config change and restart.

The configured agent profile must be the profile registered for the service's device class, and compatibility properties must match the imported update. The client does not advertise any onboarding, multi-step, reference-update, or other-handler capability.

```sh
dotnet run --project tools/SkittleSorter.Updater -- --config /etc/skittlesorter/software-updates.json
# One bounded request/report cycle for an operator-led demonstration:
dotnet run --project tools/SkittleSorter.Updater -- --config /etc/skittlesorter/software-updates.json --check-once
```

The long-running client polls no more frequently than every four hours. Use `--check-once` for an explicit demo check; do not lower the service poll interval. Each check/report holds the configured `provisioningLockPath` with an exclusive `FileStream` only while its DPS session/exchange is active. The session is disconnected/disposed before unlocking, payload installation, and polling sleep. The sorter-side DPS provisioning service must use that exact path and lock semantics before enabling this host.

After verification the updater writes the archive under the helper's fixed, unprivileged input directory and passes only its basename (never a path or shell fragment) through non-interactive `sudo` to the root-owned helper:

```text
sudo -n /usr/lib/skittlesorter/installer-helper install \
  --payload <verified-tar.gz-basename> --version <version> --runtime linux-arm64
```

The helper is responsible for validating the basename, snapshotting the payload into its root-owned store, and invoking the fixed Python installer with `--root /opt/skittlesorter`. It owns activation, health checking, and rollback. Configure narrowly scoped passwordless sudo for `/usr/bin/sudo -n /usr/lib/skittlesorter/installer-helper` only; the updater hard-codes those paths and does not permit a config-selected privileged executable. The updater runs as its normal unprivileged service account, which must be allowed to create files in `/var/cache/skittlesorter-updates/incoming/`. The accepted archive limit is 256 MiB, matching the helper and installer boundary. A nonzero helper exit is reported as an install failure. If the updater restarts during the installer boundary, it stops for operator reconciliation of `/opt/skittlesorter/.installer-journal.json`; it will not guess success from a possibly stale process or retry installation automatically.

## Supported manifest subset

After both nested RS256 signatures and the signed SHA-256 of the exact unescaped manifest bytes verify against configured root keys, the client accepts only:

- Regular update offers received using `iotdps-get-deviceupdate`.
- Manifest version 5, one inline step, one file, and handler `skittlesorter/application:1`.
- `handlerProperties.installedCriteria` exactly equal to `updateId.version`, and `handlerProperties.runtime` exactly equal to configured `linux-arm` or `linux-arm64`.
- A single file with a signed SHA-256 and exact signed size. The step's `files` entry is the file ID key in the manifest's top-level `files` map and in DPS `fileUrls`; the generator may choose the payload filename as that ID, but the ID and `fileName` are distinct fields. The file URL is only a locator, not a trust anchor; it and every redirect must use HTTPS. The body is size-limited, streamed to disk, hashed, then made eligible for installation.

Unknown handlers, multiple steps/files, onboarding jobs, reference steps, wrong runtime, and malformed metadata are rejected before installer invocation. No manifest field can select an executable or command.

## Unverified integration gates and known gaps

- No live DPS namespace, Azure registration, preview software-update service, or Raspberry Pi was available here. MQTT/TLS interoperability, service-side capability naming, imported artifact behavior, and the shared installer's real exit/rollback contract still require live gates.
- A power loss after the journal enters `Installing` but before the updater durably records the installer result leaves an ambiguous outcome. The host refuses to make up a terminal report; reconcile the installer journal/active release first.
- Trusted root rotation is operator-managed; response-provided root-key URLs are ignored. Runtime root-key-package parsing/rotation is not implemented.
- Lost MQTT sessions are recreated on checks/reports with bounded session retries; unacknowledged reports remain durable and are replayed unchanged. Exhausted transport attempts defer a daemon to its next scheduled poll. A report 409 stops the host and preserves its report/state for operator reconciliation, not acknowledgement or automatic corrective polling.
- Full C SDK machine-code error recovery, report-conflict corrective polling, and exact C SDK persistence format are not implemented. State uses the client's own versioned atomic JSON journal.
- Test coverage is local unit coverage only; it is not evidence of Azure or Pi acceptance.
