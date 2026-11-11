# Introduction: Building an IoT Device with Azure DPS, ADR, and Certificate Management

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

Welcome to this comprehensive blog series on building a real-world IoT device using Azure's latest IoT Hub, Azure Device Provisioning Service (DPS) features, along with Microsoft-managed certificate infrastructure.

<details>
<summary>Click here to see what's changed since the last preview</summary>

I've moved the sorter to Microsoft's unified
[`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4).
One `ConnectionClient` handles DPS provisioning and the Hub connection;
`TelemetryClient` sends the colour readings. That lets me remove the custom C#
DPS protocol rather than maintain it alongside the SDK.

The sorter calls the SDK directly. `AzureDpsFramework` supplies configuration,
durable CSR/key/certificate persistence and optional ADR ARM helpers; its old
session API remains for package compatibility. The series defaults to X.509
bootstrap attestation and a CSR-issued X.509 operational certificate.
Standard symmetric-key/X.509 profiles
and both CSR onboarding flows remain, as do .NET 10 and our existing Hub.
The SDK discovers its assigned endpoint and profile through DPS.
The new bounded `--cloud-self-test --config PATH` checks provisioning, connection
and telemetry without physical hardware. Retry/reprovision and optional renewal
APIs are available in the SDK; automatic certificate renewal isn't implemented.

This is still a preview. Read Microsoft's [SDK overview and feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).
The separate ADR software-updates client keeps its own DPS update protocol and
MQTTnet 4; the sorter moves to MQTTnet 5. The SDK doesn't implement that update
workflow, and this migration doesn't change our cloud resource topology.

</details>

## What You'll Build

By the end of this series, you'll have:
- ✅ A complete IoT device application (Skittle Sorter)
- ✅ Automated device provisioning with DPS
- ✅ Microsoft-managed X.509 certificate issuance
- ✅ Azure Device Registry integration
- ✅ Practical security patterns and considerations
- ✅ Full automation scripts

The series uses a **3D-printed Skittle sorter** for the physical build, but it’s **optional** — you can run everything with mocked hardware. Still, it’s a fun build if you want the full experience.

> ⚠️ **Warning:** This guide is for **testing and demo purposes only**. **Do not use self-signed X.509 certificates in production.**

This series focuses on **patterns and principles**. We’ll use self-signed certs to make the learning path simple and reproducible, but the concepts are foundational and transfer directly to production setups. In practice, you’ll replace self-signed certs with **production-grade certificates** issued by a trusted CA (or a managed service).

### Working with Preview APIs

The IoT Hub integration with Azure Device Registry (ADR) and certificate
management remain in public preview and aren't recommended for production.
The sample uses the official unified SDK for provisioning, including CSR
issuance. Our adapter keeps the configuration and certificate-file details out
of the sorting loop. Pin the preview package and check current Microsoft
documentation before upgrading.

## What's New: Azure IoT Hub with ADR (Preview)

Azure IoT Hub integration with Azure Device Registry (ADR) represents IoT Hub
devices as Azure Resource Manager resources. ADR is generally available for
Azure IoT Operations, while its IoT Hub integration and Microsoft-backed
certificate management remain in public preview. Certificate management is
optional and issues operational certificates after a separate device
onboarding credential is validated by DPS.

See the [application architecture](../docs/Architecture.md) for the component boundaries.

## What's New in 2025-2026

This series focuses on the current IoT Hub preview guidance:

### 🆕 Azure IoT Hub + Azure Device Registry (ADR) Integration (Preview)
- IoT Hub integrates with ADR to provide a **unified device registry** across IoT Hub and IoT Operations
- ADR namespaces enable centralized device metadata and identity management
- Eligible existing IoT Hubs can be connected permanently; this series creates a dedicated new Hub

### 🆕 Microsoft-Backed X.509 Certificate Management (Preview)
- ADR offers **certificate management** using Microsoft-managed PKI
- Issues and renews **operational X.509 certificates** for device authentication to IoT Hub
- Certificates are chained to Microsoft-managed Certificate Authorities (CAs)
- Devices onboard through DPS, then may receive operational certificates via policy-driven issuance
- **DPS is required** for every IoT Hub preview scenario; certificate management is optional

> ⚠️ **Preview Notice:** ADR integration and Microsoft-backed certificate management are in public preview and **not recommended for production workloads**.

### Supported Regions (Preview)
- Australia East
- Japan East
- Central US
- UK South

Starting in October 2026, ADR integration and certificate management are
available at no cost during preview; IoT Hub and DPS are billed at standard
rates. See [What's Changed since the Previous Preview](07-whats-changed-since-previous-preview.md)
for the complete current guidance and references.

## Official Docs & Announcements

- Azure IoT Hub + ADR preview announcement: https://techcommunity.microsoft.com/blog/iotblog/azure-iot-hub-with-adr-preview-extending-azure-capabilities-and-certificate-mana/4465265
- Azure IoT Hub “What’s New”: https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new
## Learning the Traditional DPS Approach

If you're interested in learning the traditional DPS workflow with self-signed X.509 certificates (without the new ADR and certificate management features), I've written a series of blog posts covering that approach:

- [Using Azure Device Provisioning Service with Self-Signed X.509 Certificates - Part 1](https://www.petecodes.co.uk/using-azure-device-provisioning-service-with-self-signed-x-509-certificates-part-1/)
## Two Ways to Use This Series

### Option 1: Quick Start (Clone and Run Automation Scripts)
```powershell
git clone https://github.com/pjgpetecodes/skittlesorter.git
cd skittlesorter

# Preview the full ADR + X.509 + CSR setup; run again without -WhatIf to create it.
.\scripts\iot-hub-device-registry\setup.ps1 `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "my-iot-rg" `
  -Location "uksouth" `
  -NamespaceName "my-adr-uniquesuffix" `
  -HubName "my-hub-001" `
  -DpsName "my-dps-001" `
  -EnrollmentId "my-device-group" `
  -WhatIf

# After setup, check the device connection without physical hardware.
dotnet run --project .\skittlesorter.csproj -- --cloud-self-test --config .\appsettings.002.json
```

See **[Creating Azure Resources](02-creating-azure-resources.md)** for detailed script examples.

### Option 2: Step-by-Step Tutorial (This Series)
Follow along post-by-post to understand **why** and **how** everything works. You'll learn:
- DPS concepts and architecture
- Azure resource setup
- X.509 certificate workflows
- .NET implementation details
7. **[What's Changed](07-whats-changed-since-previous-preview.md)** - Testing and migration notes

## Prerequisites

- **Azure Subscription** ([free trial available](https://azure.microsoft.com/free/))
- **Azure CLI** with IoT extension ([install guide](https://learn.microsoft.com/cli/azure/install-azure-cli))
- **PowerShell** 7+ ([install guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
- **.NET 10** SDK ([download](https://dotnet.microsoft.com/download))
- Basic C# and IoT concepts understanding

## Series Outline

1. **[A Primer on DPS](01-primer-on-dps.md)** - Understanding Device Provisioning Service
2. **[Creating DPS and IoT Hub](02-creating-azure-resources.md)** - Azure resource setup
3. **[X.509 and CSR Workflows](03-x509-and-csr-workflows.md)** - Certificate workflows
4. **[Configuring Enrollment Groups](04-configuring-enrollment-groups.md)** - Connecting devices
5. **[Keeping a Thin DPS Adapter](05-building-dps-framework.md)** - Replacing the custom provisioning protocol
6. **[Building the Device Application](06-building-device-application.md)** - .NET implementation and ADR metadata
7. **[What's Changed](07-whats-changed-since-previous-preview.md)** - Preview and SDK migration notes

## Why This Series?

Most IoT tutorials use **symmetric keys** or **manual X.509 certificate generation**. This series shows you:
- How to use Microsoft's **new certificate management** features
- How to use the **unified .NET device SDK** with durable application credentials
- How to integrate **ADR** for advanced device management
- How to apply **production-minded** IoT patterns that carry over to trusted, CA-issued certificates

Let's get started! 🚀

---

[Next: A Primer on DPS →](01-primer-on-dps.md)
