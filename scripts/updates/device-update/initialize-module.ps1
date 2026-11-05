[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$Hub,
    [Parameter(Mandatory)][string]$DeviceId,
    [string]$ModuleId = "adu",
    [string]$Group = "skittlesorter-pi4",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
if (!$Apply) {
    Write-Host "Preview only: create module '$ModuleId' under '$DeviceId' in '$Hub', then set its ADUGroup tag. The sorter device must already exist."
    return
}
Assert-UpdateCli
Invoke-UpdateAzure -Arguments @("iot", "hub", "device-identity", "show", "-n", $Hub, "-d", $DeviceId, "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "hub", "module-identity", "create", "-n", $Hub, "-d", $DeviceId, "-m", $ModuleId, "-o", "none")
Invoke-UpdateAzure -Arguments @("iot", "hub", "module-twin", "update", "-n", $Hub, "-d", $DeviceId, "-m", $ModuleId, "--set", "tags.ADUGroup=$Group", "-o", "none")
Write-Host "Module created and tagged. Transfer its connection string through a protected channel to the standard agent configuration; never put it in a release archive."
