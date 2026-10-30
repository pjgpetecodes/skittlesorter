# What's Changed since the Previous Preview?

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

The earlier posts in this series captured an earlier view of Azure IoT Hub's
integration with Azure Device Registry (ADR) and Microsoft-backed X.509
certificate management. This update aligns the series with the current
Microsoft Learn guidance.

**Important:** IoT Hub integration with ADR and certificate management is still
in public preview and isn't recommended for production workloads. ADR is
generally available for Azure IoT Operations, but its IoT Hub integration
remains in preview.

## Current guidance at a glance

### DPS is required for every IoT Hub preview scenario

The current guidance requires Device Provisioning Service (DPS) for all IoT
Hub preview scenarios, including deployments that use ADR without certificate
management. Certificate management is optional; DPS is not.

### Existing IoT Hubs can't be upgraded

To try ADR integration, create a new IoT Hub. Existing hubs continue operating,
but Microsoft documents no upgrade path to add the integration to an existing
hub. Deploy IoT Hub, ADR, and DPS in one of the currently supported regions:
Australia East, Japan East, Central US, or UK South.

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

Microsoft's current setup guide covers the namespace, certificate policy,
managed identities and role assignments, new IoT Hub, DPS links, policy
synchronization, and enrollment configuration. The series uses
`azure-iot` 1.0.0b2 or later with Azure CLI 2.73.0+ and Python 3.10+.
In this extension version, create Hub and DPS first, then link DPS followed by
the Hub with `az iot adr ns link add`; the create commands no longer take
namespace-link inputs. The series retains its own step-by-step Azure CLI walkthrough in
[Part 3](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3/),
updated to use that workflow. It covers preview-extension installation,
namespace certificate management, a `GEN2` hub, managed identities, namespace
linking, DPS-to-Hub linking, credential synchronization, and enrollment-policy
assignment, with verification commands along the way.

## What changed in this article series

The earlier posts included a quick-start path using the repository's setup
scripts, described some sample implementation details as requirements of the
preview, and stated that certificate revocation wasn't supported. The revised
articles now:

- Keep the CLI walkthrough in the series and update its commands and
  verification steps for `azure-iot` 1.0.0b2, including the supported-region
  list and the separate DPS-first namespace-link workflow.
- Update the PowerShell setup scripts to validate `azure-iot` 1.0.0b2 or
  later, restrict the full-setup script to supported regions, enable
  certificate management when creating ADR, and link newly created DPS and Hub
  resources to the namespace. The X.509 attestation helper now defaults to the
  `cert-policy` name created by the full-setup script.
- Clarify that DPS is required even when certificate management isn't enabled,
  and that existing IoT Hubs have no upgrade path.
- Separate the device's onboarding credential from its ADR-issued operational
  certificate.
- Reflect the documented renewal and revocation lifecycle, while noting the
  documented limitation for externally signed intermediate CAs.
- Describe the custom MQTT-based DPS framework as an implementation choice in
  this repository's sample, not a general requirement or a verified statement
  about all official client SDKs.

These edits align the articles to current documentation; they don't establish
that every documented capability was newly introduced in this preview.

## Official references

- [What's new in Azure IoT Hub?](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-what-is-new)
- [Azure Device Registry integration with IoT Hub](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-overview)
- [Deploy IoT Hub with Device Registry integration and certificate management](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup)
- [What is certificate management?](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-overview)
- [Key concepts for certificate management](https://learn.microsoft.com/en-us/azure/iot/iot-certificate-management-concepts)
