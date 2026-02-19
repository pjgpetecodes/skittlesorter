# Introduction: Building an IoT Device with Azure DPS, ADR, and Certificate Management

Welcome to this comprehensive blog series on building a real-world IoT device using Azure's latest IoT Hub, Azure Device Provisioning Service (DPS) features, along with Microsoft-managed certificate infrastructure.

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

As of the time of writing (31st January 2026), these are preview features and the official Microsoft C# SDK doesn't yet support the new DPS and certificate management capabilities. 

However, I've built a custom DPS framework that communicates directly with the preview APIs using MQTT protocol. 

It's worth noting that Microsoft does provide SDK support for Python and C/C++, but C# developers need to wait for official support or use custom implementations like ours in the meantime.

## What's New: Azure IoT Hub with ADR (Preview)

In November 2025, Microsoft announced the public preview of Azure IoT Hub integration with Azure Device Registry (ADR), bringing IoT devices under the Azure management plane with ARM resource representation. ADR includes an optional **certificate management** feature that provides Microsoft-backed X.509 PKI, eliminating the need for custom certificate infrastructure. ADR now serves as a unified control plane for managing both IoT Hub devices and Azure IoT Operations assets, with policy-driven certificate lifecycle management that automates issuance and renewal at scale.

![Azure IoT Hub with ADR Architecture](path/to/architecture-diagram.png)

## What's New in 2025-2026

This series focuses on **preview features** (starting November 2025):

### 🆕 Azure IoT Hub + Azure Device Registry (ADR) Integration (Preview)
- IoT Hub integrates with ADR to provide a **unified device registry** across IoT Hub and IoT Operations
- ADR namespaces enable centralized device metadata and identity management
- ADR integration is required for the latest provisioning and certificate management features

### 🆕 Microsoft-Backed X.509 Certificate Management (Preview)
- ADR offers **certificate management** using Microsoft-managed PKI
- Issues and renews **operational X.509 certificates** for device authentication to IoT Hub
- Certificates are chained to Microsoft-managed Certificate Authorities (CAs)
- Devices onboard through DPS, then receive operational certs via policy-driven certificate issuance
- **DPS is required** for provisioning (DPS must be linked and used for all preview scenarios)

> ⚠️ **Preview Notice:** ADR integration and Microsoft-backed certificate management are in public preview and **not recommended for production workloads**.

### Supported Regions (Preview)
- East US
- East US 2
- West US
- West US 2
- West Europe
- North Europe

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
cd skittlesorter/scripts

# Full ADR + X.509 setup (recommended)
.\setup-x509-dps-adr.ps1 `
  -ResourceGroup "my-iot-rg" `
  -Location "eastus" `
  -IoTHubName "my-hub-001" `
  -DPSName "my-dps-001" `
  -AdrNamespace "my-adr-001" `
  -UserIdentity "my-uami"

# OR X.509 only (traditional approach)
.\setup-x509-attestation.ps1 `
  -RegistrationId "my-device" `
  -DpsName "my-dps-001" `
  -ResourceGroup "my-iot-rg"

# Then run the device app
cd ..
dotnet run --project src/skittlesorter.csproj
```

See **[Creating Azure Resources](02-creating-azure-resources.md)** for detailed script examples.

### Option 2: Step-by-Step Tutorial (This Series)
Follow along post-by-post to understand **why** and **how** everything works. You'll learn:
- DPS concepts and architecture
- Azure resource setup
- X.509 certificate workflows
- .NET implementation details
7. **[Testing & Troubleshooting](07-testing-troubleshooting.md)** - Testing and operational checks

## Prerequisites

- **Azure Subscription** ([free trial available](https://azure.microsoft.com/free/))
- **Azure CLI** with IoT extension ([install guide](https://learn.microsoft.com/cli/azure/install-azure-cli))
- **PowerShell** 7+ ([install guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
- **.NET 10** SDK ([download](https://dotnet.microsoft.com/download))
- Basic C# and IoT concepts understanding

## Series Outline

1. **[A Primer on DPS](01-primer-on-dps.md)** - Understanding Device Provisioning Service
2. **[Creating DPS and IoT Hub](02-creating-dps-and-iot-hub.md)** - Azure resource setup
3. **[X.509 Certificate Hierarchy](03-x509-certificate-hierarchy.md)** - Certificate workflows
4. **[DPS Configuration & Enrollment](04-dps-configuration-enrollment.md)** - Connecting devices
5. **[Building the Device Application](05-building-device-application.md)** - .NET implementation
6. **[ADR Integration](06-adr-integration.md)** - Device Registry features
7. **[Testing & Troubleshooting](07-testing-troubleshooting.md)** - Production readiness

## Why This Series?

Most IoT tutorials use **symmetric keys** or **manual X.509 certificate generation**. This series shows you:
- How to use Microsoft's **new certificate management** features
- How to build a **custom DPS framework** to access preview APIs
- How to integrate **ADR** for advanced device management
- How to apply **production-minded** IoT patterns that carry over to trusted, CA-issued certificates

Let's get started! 🚀

---

[Next: A Primer on DPS →](01-primer-on-dps.md)
