[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Location,
    [Parameter(Mandatory)][string]$Namespace,
    [Parameter(Mandatory)][string]$Dps,
    [Parameter(Mandatory)][string]$Hub,
    [Parameter(Mandatory)][string]$UpdateInstance,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]{3,24}$')][string]$Storage,
    [Parameter(Mandatory)][string]$OperatorObjectId,
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: NEW ADR lab '$ResourceGroup': namespace, DPS-first links, separate S1 Hub, Software Updates instance. Linking is irreversible; this must NOT be the GA Device Update Hub. Add -Apply to execute."
    return
}
Assert-UpdateCli
Assert-NewUpdateResourceGroup
Invoke-UpdateAzure -Arguments @("group", "create", "-n", $ResourceGroup, "-l", $Location, "--tags", "skittlesorter-update-channel=adr", "-o", "none")
foreach ($provider in @("Microsoft.Devices", "Microsoft.DeviceRegistry", "Microsoft.DeviceUpdate", "Microsoft.Storage")) {
    Invoke-UpdateAzure -Arguments @("provider", "register", "--namespace", $provider, "--wait", "-o", "none")
}
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "create", "-n", $Namespace, "-g", $ResourceGroup, "-l", $Location, "--outbound-system-assigned-mi", "true", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "dps", "create", "-n", $Dps, "-g", $ResourceGroup, "-l", $Location, "--system-assigned-mi", "-o", "none")
$dpsId = Invoke-UpdateAzure -Arguments @("iot", "dps", "show", "-n", $Dps, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "link", "dps", "add", "-n", "primary-dps", "--ns", $Namespace, "-g", $ResourceGroup, "--dps-id", $dpsId, "--system-assigned-mi", "true", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "hub", "create", "-n", $Hub, "-g", $ResourceGroup, "-l", $Location, "--sku", "S1", "--system-assigned-mi", "-o", "none")
$hubId = Invoke-UpdateAzure -Arguments @("iot", "hub", "show", "-n", $Hub, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "link", "hub", "add", "-n", "primary-hub", "--ns", $Namespace, "-g", $ResourceGroup, "--hub-id", $hubId, "--system-assigned-mi", "true", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "su", "instance", "create", "-n", $UpdateInstance, "-g", $ResourceGroup, "-l", $Location, "--system-assigned-mi", "true", "-o", "none")
$updateId = Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "su", "instance", "show", "-n", $UpdateInstance, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "link", "su", "add", "-n", "primary-su", "--ns", $Namespace, "-g", $ResourceGroup, "--su-id", $updateId, "--system-assigned-mi", "true", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "link", "wait", "--ns", $Namespace, "-g", $ResourceGroup, "-o", "none")
Invoke-UpdateAzure -Arguments @("storage", "account", "create", "-n", $Storage, "-g", $ResourceGroup, "-l", $Location, "--sku", "Standard_LRS", "--allow-blob-public-access", "false", "--min-tls-version", "TLS1_2", "-o", "none")
Invoke-UpdateAzure -Arguments @("storage", "container", "create", "--account-name", $Storage, "--name", "updates", "--auth-mode", "key", "-o", "none")
$storageId = Invoke-UpdateAzure -Arguments @("storage", "account", "show", "-n", $Storage, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("role", "assignment", "create", "--assignee-object-id", $OperatorObjectId, "--role", "Device Update Content Administrator", "--scope", $updateId, "-o", "none")
Invoke-UpdateAzure -Arguments @("role", "assignment", "create", "--assignee-object-id", $OperatorObjectId, "--role", "Storage Blob Data Contributor", "--scope", $storageId, "-o", "none")
Write-Host "ADR links finished. Configure supported X.509 enrollment, root trust, registration and a real check/report proof before running an update job. Allow role propagation."
