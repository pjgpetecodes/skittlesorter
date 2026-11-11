# ADR Integration

Query and update devices in Azure Device Registry after provisioning.

## Capabilities
- List devices in a namespace
- Get registry-device properties, tags and system data
- Update `manufacturer`, `model`, `hardwareRevision`, `softwareRevision` and `enablementState`, plus resource tags

## Configuration-Driven Updates
- Controlled via `Adr.DeviceUpdate` section in `appsettings.json`.
- The current ARM resource is `namespaces/registryDevices`, API `2026-11-02-preview`.
- `Attributes` is a compatibility configuration container: only exact keys
  `manufacturer`, `model`, `hardwareRevision` and `softwareRevision` are accepted
  and mapped to ARM properties. Arbitrary keys are rejected explicitly.
- `DeviceEnabled` maps to `enablementState: "Enabled"`/`"Disabled"`;
  `OperatingSystemVersion` maps to `softwareRevision` and takes precedence over
  `Attributes.softwareRevision` when both are supplied.
- Use string-valued `Tags` for custom labels such as hardware ID, owner, location
  or device category, not unsupported attributes or an ARM `operatingSystemVersion`.

## Authentication on a Raspberry Pi

Device provisioning and Hub telemetry use the configured device credentials.
The optional ADR list/get/update calls use `DefaultAzureCredential` to obtain an
Azure Resource Manager token instead. A device certificate is not an ARM login.
Successfully connecting to the Hub does not imply that these management calls
have permission to run.

For normal unattended Pi operation without an ARM identity, set `Adr.Enabled`
to `false`. Keep `IoTHub.SendTelemetry` enabled. This skips only the app's ADR
management calls; it does not disable DPS/Hub connectivity or the cloud-managed
ADR registration. `Adr.DeviceUpdate.Enabled` alone controls metadata updates,
not all management requests.

For an interactive demo, install Azure CLI and sign in as the same Linux user
that runs the app. First check the architecture:

```bash
dpkg --print-architecture
lsb_release -cs
```

Microsoft provides apt packages for `arm64`, not 32-bit `armhf`. Check the
supported Debian/Ubuntu releases in the
[official Linux installation guide](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-linux?view=azure-cli-latest).
On a supported 64-bit Raspberry Pi OS/Debian installation:

```bash
sudo apt-get update
sudo apt-get install -y curl
curl -fsSL 'https://azurecliprod.blob.core.windows.net/$root/deb_install.sh' | sudo bash
az version
```

This runs Microsoft's installer with administrator privileges. Keep the URL
single-quoted so the shell does not expand `$root`.

Sign in without `sudo`:

```bash
az login --use-device-code
az account set --subscription "<subscription-id>"
dotnet run
```

Open the displayed login URL on another machine and enter the code. With
`Adr.Enabled: true`, `DefaultAzureCredential` can use `AzureCliCredential`
automatically; no IoT extension is needed for the app's ARM calls. Install
the preview-enabled `azure-iot` extension separately if you also want to run the
demo's `az iot` commands:

```bash
az extension add --name azure-iot --allow-preview true --upgrade --yes
```

The signed-in account needs read permission on the namespace/devices for
listing and getting resources, and write permission for enabled metadata
updates. A login does not grant these roles automatically. Run the app as the
same user, since a root login or a different account has a separate token cache.
An interactive CLI login can expire and is not an unattended production identity.

## Related

- [Azure Setup](./Azure-Setup.md)
- [DPS Provisioning](./DPS-Provisioning.md)
- [Configuration](./Configuration.md)