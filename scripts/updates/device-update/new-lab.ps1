[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Location,
    [Parameter(Mandatory)][string]$Hub,
    [Parameter(Mandatory)][string]$Dps,
    [Parameter(Mandatory)][string]$Account,
    [Parameter(Mandatory)][string]$Instance,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]{3,24}$')][string]$Storage,
    [Parameter(Mandatory)][string]$OperatorObjectId,
    [string]$RegistrationId = "skittlesorter-ga",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: create a NEW GA lab '$ResourceGroup' in '$Location': Hub, DPS, Device Update account/instance, private artifact storage, and data roles. Add -Apply to execute."
    return
}
Assert-UpdateCli
Assert-NewUpdateResourceGroup
Invoke-UpdateAzure -Arguments @("group", "create", "-n", $ResourceGroup, "-l", $Location, "--tags", "skittlesorter-update-channel=ga", "-o", "none")
foreach ($provider in @("Microsoft.Devices", "Microsoft.DeviceUpdate", "Microsoft.Storage")) {
    Invoke-UpdateAzure -Arguments @("provider", "register", "--namespace", $provider, "--wait", "-o", "none")
}
Invoke-UpdateAzure -Arguments @("iot", "hub", "create", "-n", $Hub, "-g", $ResourceGroup, "-l", $Location, "--sku", "S1", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "dps", "create", "-n", $Dps, "-g", $ResourceGroup, "-l", $Location, "-o", "none")
# The link needs a service credential; keep it in memory, never in the payload or console output.
$hubConnection = Invoke-UpdateAzure -Arguments @("iot", "hub", "connection-string", "show", "-n", $Hub, "-g", $ResourceGroup, "--query", "connectionString", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("iot", "dps", "linked-hub", "create", "--dps-name", $Dps, "-g", $ResourceGroup, "--connection-string", $hubConnection, "--location", $Location, "-o", "none")
$hubConnection = $null
Invoke-UpdateAzure -Arguments @("iot", "dps", "enrollment", "create", "--dps-name", $Dps, "-g", $ResourceGroup, "--registration-id", $RegistrationId, "--attestation-type", "symmetricKey", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "du", "account", "create", "-n", $Account, "-g", $ResourceGroup, "-l", $Location, "--assign-identity", "[system]", "-o", "none")
$hubId = Invoke-UpdateAzure -Arguments @("iot", "hub", "show", "-n", $Hub, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("iot", "du", "instance", "create", "--account", $Account, "--instance", $Instance, "-g", $ResourceGroup, "--iothub-ids", $hubId, "-o", "none")
Invoke-UpdateAzure -Arguments @("storage", "account", "create", "-n", $Storage, "-g", $ResourceGroup, "-l", $Location, "--sku", "Standard_LRS", "--allow-blob-public-access", "false", "--min-tls-version", "TLS1_2", "-o", "none")
Invoke-UpdateAzure -Arguments @("storage", "container", "create", "--account-name", $Storage, "--name", "updates", "--auth-mode", "key", "-o", "none")
$accountId = Invoke-UpdateAzure -Arguments @("iot", "du", "account", "show", "-n", $Account, "-g", $ResourceGroup, "--query", "id", "-o", "tsv")
$principal = Invoke-UpdateAzure -Arguments @("iot", "du", "account", "show", "-n", $Account, "-g", $ResourceGroup, "--query", "identity.principalId", "-o", "tsv")
Invoke-UpdateAzure -Arguments @("role", "assignment", "create", "--assignee-object-id", $principal, "--assignee-principal-type", "ServicePrincipal", "--role", "IoT Hub Data Contributor", "--scope", $hubId, "-o", "none")
Invoke-UpdateAzure -Arguments @("role", "assignment", "create", "--assignee-object-id", $OperatorObjectId, "--role", "Device Update Administrator", "--scope", $accountId, "-o", "none")
Write-Host "GA resources created. RBAC propagation, DPS provisioning, module-agent configuration and a Pi deployment remain separate validation gates."
