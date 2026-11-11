# Demo cheat sheet

Run commands from the repository root. The reset command requires PowerShell 7;
`dotnet` and `az` commands also work in Bash on the Pi.

## Sign in

On the machine used for Azure CLI commands:

```powershell
az login --use-device-code
az account set --subscription "<demo-subscription-id>"
az extension add --name azure-iot
```

See [ADR Integration](ADR-Integration.md#authentication-on-a-raspberry-pi)
for Azure CLI installation on a 64-bit Pi.

## 1. Clean start

Stop the sorter with **Ctrl+C** first. Each command resets one identity,
retaining enrollment, certificates, and infrastructure. Run the reset on the
device whose local `appsettings.json` matches that identity, so its cached
assignment is also cleared. Both reset examples require PowerShell 7 and Azure
CLI, including on the Pi; run `pwsh` first if your terminal is Bash.

### PC: skittlesorter

Run in PowerShell on the PC:

```powershell
$subscription = (Get-Content .\appsettings.json -Raw | ConvertFrom-Json).Adr.SubscriptionId

.\scripts\clean-test-start.ps1 `
  -AppConfigPath .\appsettings.json `
  -SubscriptionId $subscription `
  -ResourceGroup pjgiothubdemo002 `
  -DpsName pjgiothubdemodps002 `
  -IotHubName pjgiothubdemo002 `
  -EnrollmentGroupName skittlesorter-x509-rebuild-002 `
  -DeviceId skittlesorter `
  -AdrNamespace pjg-adr-namespace-002 `
  -HubAuthType key
```

Add `-WhatIf` to preview the target without deleting anything. These demo
commands explicitly use key-based Hub registry access, which requires local
Hub authentication to be enabled and permission to retrieve a registry-capable
policy key. No connection string or key needs to be copied into the command.
Omit `-HubAuthType key` to use the default Entra login-based access when your
account has Hub registry permissions. If preflight reports **Unauthorized**,
it stops before deletion; see
[Clean Test Start](Clean-Test-Start.md#multiple-demo-identities-and-authentication-failures)
for authentication requirements and manual recovery.

The reset prints timestamped Azure operations and their durations. Preflight
requests run before deletion, and ADR deletion is asynchronous. Wait for the
final `Clean demo start prepared` message before restarting the app; a device
disappearing from ADR alone does not mean Hub/DPS cleanup has finished.

### Pi: skittlesorter2

Run in PowerShell on the Pi, from its repository root:

```powershell
$subscription = (Get-Content .\appsettings.json -Raw | ConvertFrom-Json).Adr.SubscriptionId

.\scripts\clean-test-start.ps1 `
  -AppConfigPath .\appsettings.json `
  -SubscriptionId $subscription `
  -ResourceGroup pjgiothubdemo002 `
  -DpsName pjgiothubdemodps002 `
  -IotHubName pjgiothubdemo002 `
  -EnrollmentGroupName skittlesorter-x509-rebuild-002 `
  -DeviceId skittlesorter2 `
  -AdrNamespace pjg-adr-namespace-002 `
  -HubAuthType key
```

The selected configuration's `RegistrationId` must match the device being
reset. The same authentication requirements and `-WhatIf` option apply to
both commands. An Azure CLI login alone does not grant Hub registry permissions
or permission to retrieve Hub policy keys.

## 2. Run

On the **PC**, using its mock-hardware `appsettings.json` (`skittlesorter`):

```powershell
dotnet run
```

On the **Pi**, using its physical-hardware `appsettings.json` (`skittlesorter2`):

```bash
dotnet run
```

Stop either app with **Ctrl+C**. Do not copy the PC hardware settings over the
Pi's configuration.

For a bounded DPS/Hub/telemetry check without moving hardware:

```powershell
dotnet run -- --cloud-self-test
```

The cloud self-test reprovisions a reset device, so do not run it when you want
the device to remain absent for the next demonstration.

## 3. List resource-group resources

```powershell
az resource list --resource-group pjgiothubdemo002 --query "[].{Name:name,Type:type}" --output table
```

To list just the ADR devices:

```powershell
az iot adr ns device list --resource-group pjgiothubdemo002 --namespace pjg-adr-namespace-002 --query "[].name" --output json
```

CLI inventory can include ARM child resources that are not shown in the
portal's resource-group overview.

## 4. Monitor communications

Use a **second terminal** on any signed-in machine while the sorter runs.

**PC telemetry:**

```powershell
az iot hub monitor-events --hub-name pjgiothubdemo002 --device-id skittlesorter
```

**Pi telemetry:**

```powershell
az iot hub monitor-events --hub-name pjgiothubdemo002 --device-id skittlesorter2
```

**Both devices (and any other device on this Hub):**

```powershell
az iot hub monitor-events --hub-name pjgiothubdemo002
```

Stop monitoring with **Ctrl+C**.

## Related

- [Quickstart](Quickstart.md)
- [Clean Test Start](Clean-Test-Start.md)
- [Telemetry & Monitoring](Telemetry.md)
- [Troubleshooting](Troubleshooting.md)
