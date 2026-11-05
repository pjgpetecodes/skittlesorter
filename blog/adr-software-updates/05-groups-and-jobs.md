# Part 5: Deliver the same feature through a namespace group and job

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft procedure: no real group/job rollout is claimed.

Reuse the versioned sorter archives from the GA series, but generate an ADR-specific import manifest:

```powershell
python .\scripts\updates\common\new-import-manifest.py `
  --channel adr --runtime linux-arm64 --version 1.1.0 `
  --payload .\artifacts\updates\skittlesorter-1.1.0-linux-arm64.tar.gz `
  --output .\artifacts\updates\adr-1.1.0.importmanifest.json
```

This manifest selects the custom `skittlesorter/application:1` handler, not `microsoft/script:1`, and imports only the application archive. The archive is shared; the handler contract and service import are not.

An import manifest is not the signed device manifest delivered by DPS. The custom parser expects the pinned device shape: version `"5"`, a file-ID map using `fileName`, and the supported step fields. The import uses `"5.0"`, a file list and import-specific `type`/`description`. Local conversion before signing is tested, but the service's actual conversion remains unverified. Never edit a signed manifest to make it pass; establish this compatibility with a genuine signed service offer before running the demonstration.

Run `scripts\updates\adr\import-update.ps1` with the separate namespace, resource group, subscription, storage and manifest. Preview is the default. `-Apply` uses namespace Software Updates staging/import commands. Verify the import appears and is deployable; the CLI returning successfully is not device delivery.

## Group membership is cached

Set registered-device properties that identify the intended Pi cohort and architecture. Create a namespace group whose query selects only the demo device. Group queries are immutable: use a new group when the query changes.

Refresh membership after property changes. Wait for group status **Ready** and inspect the resolved filter, count and intended device identity. A preview of ten members is not a complete fleet list. For this demonstration the expected count is one.

`scripts\updates\adr\group-job.bicep` declares a group and a regular `SoftwareUpdate` job using API `2026-11-02-preview`. It takes the exact group query and runtime as inputs; it deliberately does not choose a broad default query. Deploying the template does not run the job.

Jobs are immutable too. Change their group/update by creating a new job, not by assuming an update-in-place changes a pending rollout. Use the portal compatibility review and the actual imported update identity `SkittleSorter/sorter-<runtime>/1.1.0`.

## Run, monitor and compare local state

`scripts\updates\adr\job.ps1` defaults to read-only run inspection. `-Action Run` still previews unless `-Apply` is supplied; actual execution invokes the documented schedule action with a one-hour timeout.

Inspect the run's device-level results, not just its aggregate end status. On the Pi compare the active release, running readiness version/process, persisted installed identity, owed reports and resumed 1.1.0 count telemetry. Capture redacted evidence that the GA controller was stopped and sorter telemetry coexisted with DPS update sessions.

If any prerequisite or trust proof is missing, stop before running the job. Unit tests are not a substitute for these missing cloud and physical-device observations.

[Previous](04-trust-and-recovery.md) | [Next: failure handling and future SDK](06-failures-and-sdk.md)

Source: [deploying a software update to a group](https://learn.microsoft.com/en-us/azure/iot/device-registry/how-to-deploy-software-update-group).
