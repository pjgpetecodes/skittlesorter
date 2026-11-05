[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Namespace,
    [Parameter(Mandatory)][string]$Storage,
    [Parameter(Mandatory)][string]$Manifest,
    [string]$Container = "updates",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: upload the custom application manifest and payload, then import into namespace '$Namespace'. This is NOT a GA Device Update import."
    return
}
Assert-UpdateCli
$path = Read-ApplicationImportManifest -Path $Manifest -Handler "skittlesorter/application:1"
Invoke-UpdateAzure -Arguments @("iot", "adr", "ns", "su", "software-update", "stage", "--ns", $Namespace, "-g", $ResourceGroup, "--manifest-path", $path, "--storage-account", $Storage, "--storage-container", $Container, "--then-import", "true", "-o", "none")
Write-Host "Import command finished. Verify the imported update and device compatibility before creating/running a job."
