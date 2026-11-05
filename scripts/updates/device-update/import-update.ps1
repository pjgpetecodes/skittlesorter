[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$Account,
    [Parameter(Mandatory)][string]$Instance,
    [Parameter(Mandatory)][string]$Storage,
    [Parameter(Mandatory)][string]$Manifest,
    [string]$Container = "updates",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: upload '$Manifest' and adjacent payloads, then import into '$Account/$Instance'. Staging generates temporary SAS URLs; protect the command output."
    return
}
Assert-UpdateCli
$path = Read-ApplicationImportManifest -Path $Manifest -Handler "microsoft/script:1"
Invoke-UpdateAzure -Arguments @("iot", "du", "update", "stage", "--account", $Account, "--instance", $Instance, "--storage-account", $Storage, "--storage-container", $Container, "--manifest-path", $path, "--then-import", "true", "-o", "none")
Write-Host "Import command finished. Verify the update is deployable before creating a deployment."
