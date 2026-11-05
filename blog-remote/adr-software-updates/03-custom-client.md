# Part 3: Implement the observed DPS update contract in C#

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft: wire compatibility requires real-service proof.

`AzureSoftwareUpdatesFramework` is intentionally separate from the existing provisioning transport. The old CSR transport's hard-coded API version is not a general-purpose update-channel version selector. Nor does editing a printed JSON `ApiVersion` prove the MQTT protocol changed.

The small host under `tools\SkittleSorter.Updater` composes update transport, signed-manifest verification, download, persistent workflow and a local installer boundary. It can later be replaced behind that boundary without changing how the sorter counts colours.

## Correlation and status are part of the protocol

Regular update checks publish the documented operation:

```text
$dps/registrations/POST/iotdps-get-deviceupdate/?$rid=<request-id>
```

The updater subscribes to `$dps/registrations/res/#`, correlates responses and reads their status/retry information. Result reports use `iotdps-report-deviceupdatestatus`. Checks include agent/profile/capability information, compatibility properties and installed identity; cached ETags must only represent the state actually retained.

A `200` response with no offered update metadata is a successful **no-update** result, not a malformed installation request. Authentication/configuration failures are not no-update. Transient errors respect retry guidance and bounded request timeouts; do not hammer DPS.

Use the same logical registered device as the sorter, but serialize DPS sessions through the shared provisioning lock. The sorter can keep sending Hub telemetry while the updater checks DPS. Service restart and reprovisioning coexistence still need real verification.

## Deliberately narrow capability

The custom application handler accepts the documented single inline sorter archive. It does not interpret arbitrary manifest commands. Unsupported handlers, reference steps and multistep content must fail before installation. Advertising an unimplemented capability is worse than declining an offer.

Workflow state must survive process restart. The client preserves terminal reports until acknowledged and keeps an installed update ID separate from an offered target. Duplicate offers should not reinstall a completed update. A replacement offer must not interrupt an application transaction already in progress.

A report accepted as `200` may have an empty body. A `409` conflict is **not** interchangeable with acknowledgement; preserve the owed result and surface reconciliation rather than silently discarding it.

Read source comments alongside the [pinned C design](https://github.com/Azure/azure-iot-sdk/blob/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/c/docs/eng/software-updates.md). The repository's custom persistence format is not a claim of binary compatibility with the C SDK's private state blob.

[Previous](02-preview-lab.md) | [Next: trust and recovery](04-trust-and-recovery.md)
