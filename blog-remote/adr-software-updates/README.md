# Updating the Skittle Sorter with ADR Software Updates

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference commit **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Custom C# client and sorter **1.0.0/1.1.0** are unpublished demonstration builds. Local drafts: service availability, real X.509 check/report and physical-Pi rollout are unverified.

This follow-on reuses the C# Skittle Sorter and local installer from the [GA Device Update series](../device-update-for-iot-hub/README.md). It does **not** reuse that Hub, the standard update agent or its cloud protocol.

1. [What changes from GA Device Update?](01-architecture.md)
2. [A separate preview lab](02-preview-lab.md)
3. [A custom C# DPS update client](03-custom-client.md)
4. [Trust, installation and recovery](04-trust-and-recovery.md)
5. [Deploying through groups and jobs](05-groups-and-jobs.md)
6. [Failures and future official SDK adoption](06-failures-and-sdk.md)

**What's changed?** The sorter now uses the unified
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** for provisioning and telemetry.
Its custom provisioning protocol is removed, but this separate update protocol
isn't: the SDK feature matrix still excludes software updates via DPS.
The sorter uses MQTTnet 5; the updater retains MQTTnet 4. This repository
demonstrates an unofficial, replaceable update client against a pinned published
protocol, not Microsoft's unannounced future .NET update API. It is not a
Microsoft-supported production agent.

The source is isolated under `AzureSoftwareUpdatesFramework` and `tools\SkittleSorter.Updater`. See the [runbook](../../docs/Application-Updates.md) for executable boundaries and validation gates. These articles need real service/Pi evidence before publication. Markdown and adjacent local WordPress HTML stay synchronized; there are no remote post IDs or automatic pushes.
