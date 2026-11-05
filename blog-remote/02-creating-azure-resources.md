## **Creating DPS, IoT Hub and ADR Instances**

> **Release baseline:** This series uses the latest Azure IoT CLI preview
> extension, `azure-iot` **1.0.0b2**, released **6 October 2026**. Last
> reviewed **29 October 2026**. [Release notes](https://github.com/Azure/azure-iot-cli-extension/releases/tag/v1.0.0b2).

Welcome back to this series of posts exploring the latest features of Azure IoT Hub, Device Provisioning Service (DPS), and Azure Device Registry (ADR).

In the previous post we dove deeper into DPS and what's new.

```wordpress-block
<!-- wp:heading {"textColor":"#f6f6f4"} -->
<h2 class="wp-block-heading has-f-6-f-6-f-4-color has-text-color">Contents:</h2>
<!-- /wp:heading -->
```

```wordpress-block
<!-- wp:list {"ordered":true,"textColor":"#f6f6f4"} -->
<ol class="wp-block-list has-f-6-f-6-f-4-color has-text-color"><!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-1" target="_blank" rel="noreferrer noopener">Introduction </a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-2" target="_blank" rel="noreferrer noopener">A DPS Primer</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-3" target="_blank" rel="noreferrer noopener">Creating DPS, IoT Hub and ADR Instances</a> (This Post)</li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4" target="_blank" rel="noreferrer noopener">Understanding X.509 and CSR Workflows</a></li>
<!-- /wp:list-item -->

<!-- wp:list-item -->
<li><a href="https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-5" target="_blank" rel="noreferrer noopener">The Device Application</a></li>
<!-- /wp:list-item --></ol>
<!-- /wp:list -->
```

### Time to Build!

The refreshed public preview brings Device Registry, IoT Hub, DPS, and
certificate management together in a new setup flow. This article updates the
previous resource-creation steps to use the current Azure CLI commands and
system-assigned identities.

> **Preview and cost warning:** IoT Hub integration with ADR and certificate
> management is still in public preview and isn't recommended for production.
> Standard S1 IoT Hub and DPS resources incur normal Azure charges. Review
> pricing and clean up dedicated test resources when you're finished.

### Prerequisites

- An Azure subscription and permission to create resources and role
  assignments at the required scopes.
- Azure CLI 2.73.0 or later, Python 3.10 or later, and PowerShell 7 or later.
- The preview-enabled `azure-iot` extension, version 1.0.0b2 or later.
- One of the preview-supported regions: `australiaeast`, `japaneast`,
  `centralus`, or `uksouth`.

The refreshed workflow creates a new standard S1 IoT Hub; it does not upgrade
an existing Hub. DPS is required for the IoT Hub preview scenarios covered by
the current guide. Certificate management is optional in the broader preview,
but is included in this certificate-focused example.

### Run the end-to-end setup script

The repository includes a resumable PowerShell script that creates the
resource group, ADR namespace, Microsoft-managed root and intermediate CAs,
certificate policy, DPS, S1 IoT Hub, namespace links, and a policy-bound DPS
enrollment group. It leaves device records untouched: creating an enrollment
group does not enroll or provision a device.

From the repository root, sign in, select your subscription, and preview the
parameters with `-WhatIf`:

```powershell
az login
az account set --subscription "<subscription-id>"

.\scripts\iot-hub-device-registry\setup.ps1 `
  -SubscriptionId "<subscription-id>" `
  -ResourceGroupName "my-iot-preview-rg" `
  -Location "uksouth" `
  -NamespaceName "my-iot-adr-namespace-002" `
  -HubName "my-iot-hub-002" `
  -DpsName "my-iot-dps-002" `
  -EnrollmentId "my-enrollment-group-002" `
  -WhatIf
```

After checking the names, subscription, and region, run the same command
without `-WhatIf`. The script installs/upgrades the preview-enabled Azure IoT
CLI extension, registers the required resource providers, creates missing
resources, links DPS first and then IoT Hub to the namespace, and waits for
both links. It can resume after a partial run by keeping existing named
resources, but it doesn't reconcile their settings; inspect reused resources
before proceeding.

The complete parameter reference, costs, and verification instructions are in
the [setup README](https://github.com/pjgpetecodes/skittlesorter/blob/main/scripts/iot-hub-device-registry/README.md).
For manual commands and the latest API and CLI requirements, use Microsoft's
[Azure CLI deployment guide](https://learn.microsoft.com/en-us/azure/iot-hub/iot-hub-device-registry-setup?pivots=azure-cli).

### What the workflow creates

1. A resource group and ADR namespace with a system-assigned identity.
2. A Microsoft-managed root CA, intermediate CA, and certificate policy.
3. Standard S1 DPS and a new standard S1 IoT Hub, both with system-assigned
   identities.
4. A namespace link to DPS, followed by a namespace link to IoT Hub.
5. A DPS enrollment group attached to the ADR certificate policy.

The setup script does not create device records, device certificates, or
provision a device. The enrollment group is only a template for future
provisioning. The operational certificate policy is separate from the
onboarding credential a device will use with DPS.

### Verify the setup

The script waits for both namespace links and prints the configured links and
certificate authorities. In the Azure portal, confirm that the resource group
contains the new resources, the namespace shows both connected services, DPS
shows the enrollment group, and IoT Hub lists the linked CA certificate.

To confirm that no ADR devices were created, list the devices in the namespace;
a fresh environment should return an empty list. Keep the resource group only
if you intend to retain the billable S1 services. Before deleting it, verify
that it contains only resources you intend to remove.

Next up we explain onboarding credentials, operational certificates, and the
CSR workflow in [Part 4](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/).

---

[Next: Creating X.509 Certificates >](https://www.petecodes.co.uk/iot-hub-with-adr-and-certificate-management-part-4/)
