# Part 1: Updating a C# Skittle Sorter without updating its operating system

> **Release baseline, reviewed 30 October 2026:** latest GA agent release inspected: **1.4.0, 25 June 2026**; Azure IoT CLI **1.0.0b2, 6 October 2026**. Sorter **1.0.0/1.1.0** are demonstration build versions, not published releases. Draft: cloud and physical-Pi validation remain outstanding.

Our sorter already reads a colour sensor, moves two servos and sends IoT Hub telemetry. The update demonstration keeps that application in C#. We are updating its published binaries, not replacing Raspberry Pi OS, installing firmware or converting the application into an IoT Edge module.

**What's changed in the sorter?** Provisioning and telemetry now use Microsoft's
unified `Microsoft.Azure.Iot.Device` **2.0.0-preview4**, with a thin
`AzureDpsFramework` configuration/persistence/session adapter. .NET 10 and the
existing Hub stay unchanged, with the assigned endpoint/profile discovered
through DPS. Standard symmetric-key provisioning still
sets `RequestOperationalCertificate: false`; the GA update agent and its module
identity are not replaced by this SDK migration.

There are three separate responsibilities. The sorter controls hardware and publishes telemetry. Microsoft's standard Linux Device Update agent receives GA deployments. Our local Python installer stages an application release, waits for a safe sorter stop, switches releases and checks readiness. The updater is outside the release being replaced.

The agent connects as a **module identity** underneath the sorter device. The sorter keeps its normal device connection; two clients must not compete for the same IoT Hub device MQTT identity. DPS provisions the sorter, but does not create that agent module. We create it explicitly after the device exists.

## What actually changes?

Build 1.0.0 exposes a canonical application version and participates in the service lifecycle. Build 1.1.0 additionally records per-colour and total **session** counts. A count is recorded after the drop completes, not simply when a sensor reports a colour. `None` detections do not count. Counts reset when the application restarts; they are not lifetime production totals.

Telemetry retains colour, timestamp and device identity, adds `appVersion`, and in the feature build adds `sessionTotal` and a `sessionCounts` snapshot. The payload is useful evidence of resumed operation, but telemetry alone does not establish that every physical movement succeeded.

## Keep the lab independent

The GA lab has its own resource group, standard IoT Hub, DPS, Device Update account/instance and private artifact storage. It does not require the ADR-issued operational-certificate flow to be finished. Its explicit standard-DPS profile sets `RequestOperationalCertificate` to `false`; existing profiles keep their original certificate-request default.

The [ADR follow-on](../adr-software-updates/01-architecture.md) uses the same app releases and installer, but a separate cloud environment and a custom preview C# update client. **Do not attach GA Device Update and ADR Software Updates to the same Hub.** Sharing application logic is not sharing update protocols.

We also exclude arbitrary update scripts from the custom client, OS changes, configuration migrations and durable production counts. Rollback restores an application release; it cannot reverse an imaginary future database migration.

[Next: Azure lab and Pi preparation](02-lab-and-pi.md)

Sources: [Device Update overview](https://learn.microsoft.com/en-us/azure/iot-hub-device-update/understand-device-update), [agent provisioning](https://learn.microsoft.com/en-us/azure/iot-hub-device-update/device-update-agent-provisioning).
