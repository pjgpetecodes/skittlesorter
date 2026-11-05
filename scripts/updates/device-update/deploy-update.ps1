[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$Account,
    [Parameter(Mandatory)][string]$Instance,
    [Parameter(Mandatory)][string]$Group,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,73}$')][string]$Deployment,
    [Parameter(Mandatory)][ValidateSet("linux-arm", "linux-arm64")][string]$Runtime,
    [string]$Version = "1.1.0",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: deploy SkittleSorter/sorter-$Runtime/$Version to group '$Group'. Verify resolved module membership and compatible device classes first; add -Apply only after review."
    return
}
Assert-UpdateCli
Invoke-UpdateAzure -Arguments @("iot", "du", "device", "deployment", "create", "--account", $Account, "--instance", $Instance, "--group-id", $Group, "--deployment-id", $Deployment, "--update-provider", "SkittleSorter", "--update-name", "sorter-$Runtime", "--update-version", $Version, "--download-security", "https", "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "du", "device", "deployment", "show", "--account", $Account, "--instance", $Instance, "--group-id", $Group, "--deployment-id", $Deployment)
