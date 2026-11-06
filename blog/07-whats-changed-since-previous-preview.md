# What's Changed since the Previous Preview?

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **30 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

The earlier posts in this series captured an earlier view of Azure IoT Hub's
integration with Azure Device Registry (ADR) and Microsoft-backed X.509
certificate management. This update aligns the series with the current
Microsoft Learn guidance.

## What's changed in the device implementation?

I've now migrated provisioning and telemetry to the official unified
[`Microsoft.Azure.Iot.Device` **2.0.0-preview4**](https://www.nuget.org/packages/Microsoft.Azure.Iot.Device/2.0.0-preview4)
package. `ConnectionClient` owns DPS provisioning and the Hub connection;
`TelemetryClient` shares it. The custom C# security providers, transports,
registration/SAS-token protocol and `Microsoft.Azure.Devices.Client` are removed.

`AzureDpsFramework` remains a thin configuration, certificate/CSR persistence,
ADR ARM and `UnifiedDeviceSession` adapter library. Standard symmetric-key/X.509
profiles and both CSR onboarding flows remain. CSR/key, issued PEM/chain and the
`.connection.json` assignment sidecar survive restarts. A required-certificate
wrapper rejects missing issuance before Hub authentication; the coordinator
holds the updater lock only during DPS sessions, including reprovisioning.

.NET 10, the existing Hub and cloud topology stay put. The SDK discovers its
assigned endpoint/profile through DPS. The sorter uses
MQTTnet 5; the separate updater retains MQTTnet 4 and its own DPS **software-update**
protocol. The SDK does not implement that workflow.

The bounded `--cloud-self-test --config PATH` performs a two-minute,
hardware-free provision/connect/one-telemetry-ack/disconnect check. The new helper
uses `-AppConfigPath` and resets only an exact device/registration when explicit
`-ResetDevice` and matching resource names are supplied, never a resource group,
enrollment group or CA. Setup now generates CSR-enabled configuration; standard
profiles explicitly set `RequestOperationalCertificate: false`.

The SDK offers retry/reprovision and optional IoT Hub renewal APIs, but I haven't
implemented automatic operational-certificate renewal. This is still a preview;
local tests don't establish a successful live Azure or Pi run. See Microsoft's
[SDK design/feature matrix](https://github.com/Azure/azure-iot-sdk/blob/main/dotnet/README.md)
and [unified samples](https://github.com/Azure/azure-iot-sdk/tree/releases/public-preview/dotnet/samples/Unified).

**Important:** IoT Hub integration with ADR and certificate management is still
in public preview and isn't recommended for production workloads. ADR is
generally available for Azure IoT Operations, but its IoT Hub integration
remains in preview.

## Current guidance at a glance

### DPS is required for every IoT Hub preview scenario

The current guidance requires Device Provisioning Service (DPS) for all IoT
Hub preview scenarios, including deployments that use ADR without certificate
management. Certificate management is optional; DPS is not.

### Eligible existing resources can be connected

The current getting-started guide supports connecting eligible existing DPS and
standard-tier IoT Hub resources, or creating new ones. Connecting them to a
namespace is permanent. Connect DPS first, then the Hub, and verify the current
supported-region list before creating the environment. For the new update demonstrations,
use dedicated resources; a Hub associated with GA Device Update cannot also be
used for ADR Software Updates.

Starting in October 2026, ADR integration and certificate management are
available at no cost during preview. IoT Hub and DPS are billed at their
standard rates.

### Onboarding and operational certificates are different

Certificate management issues and manages **operational** certificates used
for device authentication after onboarding. A device still uses a separate
onboarding credential with DPS; supported onboarding methods include
third-party X.509 certificates, symmetric keys, and TPM. During provisioning,
the device submits a CSR and DPS coordinates issuance through the ADR
namespace's certificate policy.

The current documentation describes certificate issuance, renewal, and
revocation, as well as CA synchronization with linked IoT Hubs. It supports a
cloud PKI with one root CA and up to three intermediate CAs per namespace, and
also describes bringing an external root CA. Revocation behavior has
limitations for intermediate CAs signed by an external CA.

### Updated CLI walkthrough

Microsoft's current setup guide covers the namespace, connected services,
managed identities and role assignments, with optional certificate management and
software updates. The series uses
`azure-iot` 1.0.0b2 or later with Azure CLI 2.73.0+ and Python 3.10+.
The refreshed certificate walkthrough in
[Part 3](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/)
uses system-assigned identities, a new standard S1 Hub and DPS, Microsoft-managed
root and intermediate CAs, and separate namespace-link commands. Its
[PowerShell setup script](https://github.com/pjgpetecodes/skittlesorter/blob/main/scripts/iot-hub-device-registry/setup.ps1)
links DPS first, then the Hub, and creates a policy-bound enrollment group
without creating device records. Its
[README](https://github.com/pjgpetecodes/skittlesorter/blob/main/scripts/iot-hub-device-registry/README.md)
documents prerequisites, supported regions, costs, and verification.

## What changed in this article series

The earlier posts included a quick-start path using retired setup commands,
described some sample implementation details as requirements of the preview,
and stated that certificate revocation wasn't supported. The revised articles
now:

- Replace the retired `GEN2`, user-assigned-identity, and combined-link
  commands in Part 3 with the refreshed standard-S1, system-assigned-identity,
  separate-link workflow.
- Add a resumable end-to-end PowerShell setup script and instructions. It
  restricts deployment to supported preview regions, creates the managed CA
  hierarchy and policy, links DPS before IoT Hub, and creates an enrollment
  group without creating devices.
- Clarify that DPS is required even when certificate management isn't enabled,
  and correct the older claim that eligible existing IoT Hubs cannot be linked.
- Separate the device's onboarding credential from its ADR-issued operational
  certificate.
- Reflect the documented renewal and revocation lifecycle, while noting the
  documented limitation for externally signed intermediate CAs.
- Replace the custom C# DPS provisioning protocol with the official unified SDK,
  retaining configuration, persistence and ADR ARM operations as a thin adapter.

These edits do not establish that every documented capability was newly
introduced in this preview, or that the downstream certificate-issuance
walkthrough and device protocol have been verified against every newer preview
API.

## Separate application-update demonstrations

The sorter now also has an application-update workstream: a
[six-part GA Device Update series](device-update-for-iot-hub/README.md), followed by
a [separate ADR Software Updates series](adr-software-updates/README.md).
They share the same C# sorter, versioned releases and local transactional
installer, but use separate Azure resources and update clients.

The app adds safe cycle-boundary shutdown, external configuration, readiness,
version telemetry and an optional session-count feature release. GA provisioning
can explicitly opt out of operational certificate issuance. The custom C# ADR
update client remains isolated from provisioning and retains its own DPS update
protocol. The unified .NET SDK does not implement software updates, and this
migration does not declare automatic certificate renewal complete.

Both series are local implementation-backed drafts: real Azure/Pi update and
failure evidence is still required. Their Markdown and adjacent local WordPress
HTML are generated together; nothing is automatically published.

## Official references

- [Current ADR connected-services setup](https://learn.microsoft.com/en-us/azure/iot/device-registry/get-started-azure-device-registry)
- [What's new in Azure IoT Hub?](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new)
- [Azure Device Registry integration with IoT Hub](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-overview)
- [Deploy IoT Hub with Device Registry integration and certificate management](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)
- [What is certificate management?](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview)
- [Key concepts for certificate management](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-concepts)
