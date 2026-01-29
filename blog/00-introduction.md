# Introduction: Building an IoT Device with Azure DPS, ADR, and Certificate Management

Welcome to this comprehensive blog series on building a real-world IoT device using Azure's latest Device Provisioning Service (DPS) features, Azure Device Registry (ADR), and Microsoft-managed certificate infrastructure.

## What You'll Build

By the end of this series, you'll have:
- ✅ A complete IoT device application (Skittle Sorter)
- ✅ Automated device provisioning with DPS
- ✅ Microsoft-managed X.509 certificate issuance
- ✅ Azure Device Registry integration
- ✅ Practical security patterns and considerations
- ✅ Full automation scripts

> ⚠️ **Warning:** This guide is for **testing and demo purposes only**. **Do not use self-signed X.509 certificates in production.**

This series focuses on **patterns and principles**. We’ll use self-signed certs to make the learning path simple and reproducible, but the concepts are foundational and transfer directly to production setups. In practice, you’ll replace self-signed certs with **production-grade certificates** issued by a trusted CA (or a managed service).

## What's New in 2025-2026

This series focuses on **preview features** (starting November 2025):

### 🆕 Azure IoT Hub + Azure Device Registry (ADR) Integration (Preview)
- IoT Hub integrates with ADR to provide a **unified device registry** across IoT Hub and IoT Operations
- ADR namespaces enable centralized device metadata and identity management
- ADR integration is required for the latest provisioning and certificate management features

### 🆕 Microsoft-Backed X.509 Certificate Management (Preview)
- ADR offers **certificate management** using Microsoft-managed PKI
- Issues and renews **operational X.509 certificates** for device authentication to IoT Hub
- Devices still onboard using a different credential, then receive operational certs after provisioning
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

## Two Ways to Use This Series

### Option 1: Quick Start (Clone and Run)
```powershell
git clone https://github.com/yourusername/skittlesorter.git
cd skittlesorter
# Follow README.md for one-command setup
```

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
