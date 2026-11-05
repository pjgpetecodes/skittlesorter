[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Subscription,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Namespace,
    [Parameter(Mandatory)][string]$Job,
    [ValidateSet("Inspect", "Run")][string]$Action = "Inspect",
    [switch]$Apply
)
. (Join-Path $PSScriptRoot "..\common\azure-helpers.ps1")
foreach ($segment in @($Subscription, $ResourceGroup, $Namespace, $Job)) {
    if ($segment -match '[/\\?#]') { throw "Resource identifiers must be individual ARM path segments." }
}
$uri = "https://management.azure.com/subscriptions/$Subscription/resourceGroups/$ResourceGroup/providers/Microsoft.DeviceRegistry/namespaces/$Namespace/jobs/$Job"
if ($Action -eq "Run") {
    if (!$Apply) {
        Write-Host "Preview only: schedule ADR job '$Job' immediately with a one-hour timeout. Refresh and verify the group's Ready status, members and compatibility first."
        return
    }
    # A stopped cloud job does not promise to interrupt a payload already being installed.
    Invoke-UpdateAzure -Arguments @("rest", "--method", "post", "--uri", "$uri/schedule?api-version=2026-11-02-preview", "--body", '{"timeout":"PT1H"}')
}
else {
    Invoke-UpdateAzure -Arguments @("rest", "--method", "get", "--uri", "$uri/runs?api-version=2026-11-02-preview")
}
