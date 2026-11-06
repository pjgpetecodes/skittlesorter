# Application updates: two independent demonstrations

> Reviewed 30 October 2026. Local implementation and article drafts, not completed
> Azure/Pi demonstrations. App build versions 1.0.0/1.1.0 are unpublished. Research
> baseline: GA agent 1.4.0 (25 June 2026), azure-iot 1.0.0b2 (6 October 2026);
> custom ADR contract at SDK commit
> `3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5`, DPS `2026-11-02-preview`.

## What is shared, and what is not

### Provisioning SDK migration, not an update-protocol replacement

The sorter now pins `Microsoft.Azure.Iot.Device` **2.0.0-preview4** and uses
unified `ConnectionClient`/`TelemetryClient` for DPS provisioning and Hub telemetry.
`AzureDpsFramework` remains a thin configuration, persistence, ADR ARM and session
adapter. Standard symmetric-key/X.509 profiles and both CSR onboarding flows remain.
.NET 10 and existing cloud resources are unchanged; the SDK discovers the assigned
Hub endpoint and connection profile through DPS.

The SDK's [feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
still excludes **software updates via DPS**. The separate
`AzureSoftwareUpdatesFramework` updater retains its own update MQTT protocol and
MQTTnet 4; the sorter uses MQTTnet 5. This migration neither replaces that updater
nor implements automatic operational-certificate renewal.

| Boundary | GA Device Update | ADR Software Updates preview |
|---|---|---|
| Application | Same C# sorter, baseline and session-count builds | Same release archive |
| Update receiver | Microsoft's standard Linux agent | Unofficial isolated C# DPS update client |
| Cloud identity | Agent module under sorter device | Registered device's X.509 DPS update channel |
| Resources | Dedicated GA Hub/DPS/update account/instance | Different Hub/DPS/namespace/update instance |
| Handler | `microsoft/script:1`, fixed local adapter | `skittlesorter/application:1`, one inline archive |
| Installation | Shared transactional local installer | Same installer |
| Article series | [Six GA posts](../blog/device-update-for-iot-hub/README.md) | [Six preview posts](../blog/adr-software-updates/README.md) |

Never connect both update services to the same Hub, or run two installation
controllers on the Pi. No OS/firmware, arbitrary manifest shell execution,
configuration migration or lifetime production counts are included.

## Build and local validation

Use .NET 10 SDK, Python 3 and PowerShell 7 on the operator computer.

```powershell
dotnet test .\tests\SkittleSorter.Tests\SkittleSorter.Tests.csproj
dotnet test .\tests\AzureSoftwareUpdatesFramework.Tests\AzureSoftwareUpdatesFramework.Tests.csproj
python -m unittest discover -s .\tests\updates -v
python -m unittest discover -s .\tests\installer -v
.\scripts\updates\common\publish-release.ps1 -Runtime linux-arm64 -Version 1.0.0 -SessionCounts $false
.\scripts\updates\common\publish-release.ps1 -Runtime linux-arm64 -Version 1.1.0 -SessionCounts $true
```

Run installer tests on Linux as well; Linux-only tests must not be counted as
passing when skipped on Windows. The optional real-process lifecycle test runs on
Linux when `SKITTLESORTER_TEST_EXECUTABLE` points at a self-contained Linux build.
It uses a generated mock-only configuration and checks SIGTERM completes the
current drop before exiting. It is not a physical servo test.

The app's `--version` reports the assembly version, stripping build metadata.
`--self-test --config PATH` checks JSON without hardware/network.
`--ready-file PATH` writes `{version, processId, readyAt}` after initialization.
When telemetry is requested in this service profile, failed provisioning cannot
produce a healthy readiness marker.

1.0.0 disables session counting at compile time. 1.1.0 counts completed drops and
adds `sessionCounts`/`sessionTotal` telemetry alongside `appVersion`. Restart resets
counts. `None` detections never count; total equals the sum of colour counts.

Build output and archives exclude actual appsettings. The packager rejects
configuration, key/certificate files and symlinks. Inspect release contents before
distribution; no source-control ignore rule substitutes for artifact inspection.

## Prepare the Pi without assuming 64-bit userland

Run `scripts/updates/common/pi-preflight.sh` on the Pi. Record `/etc/os-release`,
`dpkg --print-architecture`, `getconf LONG_BIT`, available space and actual agent
package version. Use `linux-arm64` for ARM64 userland or `linux-arm` for ARMHF.
Check both .NET 10 and agent compatibility with the actual OS before proceeding.

Read the [installer bootstrap/recovery scripts](../deploy/updates/) and review the
systemd templates under `deploy/systemd`. Keep release directories under `/opt`,
configuration under `/etc/skittlesorter` and state under `/var/lib/skittlesorter`.
Use an unprivileged sorter identity with only its required hardware permissions;
do not grant the network updater arbitrary passwordless sudo.

For the custom updater, bootstrap the constrained helper with
`deploy/updates/install-installer-helper.sh` after reviewing its account/path
settings. Its only privileged command is:

```text
/usr/bin/sudo -n /usr/lib/skittlesorter/installer-helper install
  --payload <basename> --version <version> --runtime <linux-arm|linux-arm64>
```

The updater writes downloads under `/var/cache/skittlesorter-updates/incoming`.
The helper takes a no-follow snapshot into a root-owned payload directory before
calling the fixed installer; arbitrary root/configuration paths and shell
commands are not accepted. The GA script handler already executes its trusted
adapter as root and uses the direct installer CLI instead.

The local journal is `/opt/skittlesorter/.installer-journal.json` and its lock is
`/opt/skittlesorter/.installer.lock`. The sorter service's privileged `ExecStartPre`
attempts interrupted-install recovery; it does not block behind an active
installer that is starting that service.

The app must finish its current cycle on SIGTERM. A service-stop timeout aborts
installation, not force-kills moving hardware. Verify rollback and journal
recovery before an attended real-hardware deployment.

## GA operator sequence

1. Review `scripts\updates\device-update\new-lab.ps1` parameters. Its default is a
   descriptive preview; `-Apply` creates a **new** resource group and billable lab.
   The script refuses an existing resource group and never repurposes the ADR Hub.
   The Hub permits shared access policies for IoT Explorer and DPS; protect its
   policy keys.
2. Configure standard DPS using the safe device-update template, an individual
   device key and `RequestOperationalCertificate: false`. Existing profiles retain
   their certificate-request default. Enable real hardware/telemetry explicitly.
3. Provision the sorter, then run `initialize-module.ps1`. Configure the selected
   official agent with its module credential and matching compatibility properties.
   DPS does not create that module. Protect credentials outside release files.
4. Install/verify baseline locally, generate `--channel ga` import metadata with
   `scripts\updates\common\new-import-manifest.py`, then use `import-update.ps1`.
   Payloads and the script adapter must sit beside the manifest.
5. Inspect import, module-twin group membership and architecture/device class.
   Use `deploy-update.ps1` with an explicit one-device group; only `-Apply` submits.
6. Compare device-level cloud results with active release, running readiness and
   resumed feature telemetry. Repeat tamper rejection, failed startup, rollback,
   restart and interruption exercises.

Staging uses account-key access and temporary SAS URLs; protect command output.
Read-only inspection is safe, but do not use `--debug` with credential-bearing
commands in public demonstrations. No Azure resource operation has been executed
as part of writing this runbook.

## ADR operator sequence and live gates

1. Stop the GA controller. Use `scripts\updates\adr\new-lab.ps1` for new, isolated
   resources, with DPS-first namespace links and an S1 Hub. Linking is irreversible.
   The Hub permits shared access policies; protect its policy keys. Check supported
   region, resource-provider registration, identities and roles.
2. Establish supported X.509 enrollment and normal registered-device provisioning.
   This is independent of finishing the older CSR issuance/renewal walkthrough.
   Obtain the real trusted signing-key baseline out of band.
3. Follow [custom updater configuration](../tools/SkittleSorter.Updater/README.md).
   Configure the same absolute provisioning lock path in app and updater.
   Establish actual authenticated check/report and Hub-telemetry coexistence.
   The future-dated preview API identifier must be available in the target service;
   published source alone is not proof of regional service availability.
4. Generate `--channel adr` metadata and use the ADR `import-update.ps1`, not the GA
   import command. This client supports only its known inline app handler.
5. Deploy `scripts\updates\adr\group-job.bicep` with an explicit query, new group/job
   names and runtime. Refresh group membership, wait for Ready and inspect the
   expected device count/identity and compatibility in the portal.
6. `job.ps1` defaults to run inspection. `-Action Run -Apply` schedules only after
   operator review. Observe per-device outcome and compare local installed ID,
   actual app version, rollback and durable report replay.

The client does not open a second Hub connection. Report conflicts are not
acknowledgements. No network trust-on-first-use, verification bypass or official
future .NET API is supplied. Inspect the framework/host READMEs for deliberate
preview limits; fixture tests are not Microsoft service-signature proof. Root
rotation/revocation is operator-managed, not an implemented signed root-package
refresh. Each check/report reconnects in a bounded, short-lived DPS session and
replays owed reports without dropping them. The shared lock is released before
download, installation or polling sleep. Report-conflict corrective polling
remains an operator reconciliation gate.
Restart at the installer boundary stops for journal reconciliation instead of
guessing success. These are explicit reasons this is not a production-ready
or completed end-to-end ADR demonstration.

## Article synchronization and publication boundary

Edit Markdown under the two new `blog-remote` series folders, then run:

```powershell
.\scripts\updates\common\sync-articles.ps1 `
  -Exporter C:\repos\petecodes\wordpress-markdown-sync\bin\wp-md.mjs
```

This exports adjacent local WordPress HTML from those exact Markdown files and
mirrors the draft Markdown under `blog`. It never pushes to WordPress, assigns
remote IDs or updates a remote baseline. Existing article backups are preserved.

## Outstanding evidence

Actual Pi OS/architecture and selected agent-package compatibility, real resource
provisioning/import/deployment, X.509 DPS update check/report, authentic service
signatures, namespace group/job delivery, physical safe shutdown, healthy rollout,
power-interruption recovery and restart/report replay remain hardware/cloud gates.
The posts label these as drafts rather than inventing screenshots or success logs.
No live publishing, commits or automatic cloud cleanup are performed.
