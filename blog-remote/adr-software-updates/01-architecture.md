# Part 1: Same C# sorter, different update service

> **Preview baseline, reviewed 30 October 2026:** Azure IoT CLI **1.0.0b2, released 6 October 2026**; DPS update API **2026-11-02-preview**; SDK reference **3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5**, inspected 30 October 2026. Client and sorter **1.0.0/1.1.0** are unpublished demo builds. Draft: Azure/Pi proof remains outstanding.

The GA series used Microsoft's standard Linux Device Update agent as an IoT Hub module. ADR Software Updates instead organizes software updates through a Device Registry namespace, groups and jobs, and offers updates over a DPS channel. A common application archive does not make these protocols interchangeable.

Use a **separate Hub, DPS, namespace, software-update instance and artifact storage**. During this preview, a Hub associated with GA Device Update cannot also be used for ADR Software Updates. We keep the original GA environment intact rather than deleting its update instance to force reuse.

On the Pi, use one local application installer and only one active cloud update controller. Stop the GA agent before enabling the custom updater. This prevents competing installation requests; the installer's local serialization is a second safeguard, not a reason to run both controllers.

## What “C# support” means here

A service that delivers application files can update a C# program without offering a native C# client API for its own device protocol. The inspected unified .NET SDK README lists software updates via DPS as unsupported. The corresponding C SDK contains the protocol implementation and examples.

**What's changed since the provisioning articles?** The sorter now uses
`Microsoft.Azure.Iot.Device` **2.0.0-preview4** for DPS provisioning and telemetry,
directly, using `AzureDpsFramework` credential helpers. That removes the custom **provisioning**
protocol, not this separate DPS **software-update** client. The sorter uses
MQTTnet 5; `AzureSoftwareUpdatesFramework` retains MQTTnet 4. .NET 10 and each
demonstration's existing cloud resources are unchanged. The SDK discovers the
assigned Hub endpoint/profile through DPS.

We still implement an isolated educational C# update client against the published
wire contract. We do not invent a future .NET update API or imply that Microsoft
supports this custom agent. Recheck the [official SDK matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
before reproducing the article.

## Separate lifecycles

The sorter retains its IoT Hub telemetry connection. The updater uses X.509 credentials on a DPS update channel and does not create another Hub connection as the same device. Because app registration and update checking may use the same DPS registration/client identity, both processes must coordinate their DPS sessions using the same exclusive lock. The unified-session coordinator holds that lock only during DPS sessions, including SDK reprovisioning, not for the lifetime of the Hub connection.

The updater survives replacement of the sorter executable. Its workflow state, terminal reports, trusted keys and certificates remain outside application release directories. The common installer verifies and activates a versioned release; the updater decides whether signed content is acceptable and reports the actual outcome.

The first implementation supports **regular registered-device updates**, one known inline application step and one payload. Onboarding updates, reference steps, arbitrary shell handlers and multistep execution are excluded. It must reject unsupported work before touching the app.

[Next: separate preview lab](02-preview-lab.md)

Sources: [ADR Software Updates](https://learn.microsoft.com/en-us/azure/iot/device-registry/concept-software-updates), [pinned .NET capability matrix](https://github.com/Azure/azure-iot-sdk/blob/3f347e4b7687745cc2c43d1e75e5da5a1a4e35f5/dotnet/README.md).
