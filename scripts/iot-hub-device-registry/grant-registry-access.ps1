#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$HubResourceId,
    [Parameter(Mandatory = $true)]
    [guid]$OperatorObjectId,
    [ValidateSet('User', 'ServicePrincipal', 'Group')]
    [string]$PrincipalType = 'User'
)

$ErrorActionPreference = 'Stop'
$role = 'IoT Hub Registry Contributor'
$existing = & az role assignment list --assignee-object-id $OperatorObjectId --scope $HubResourceId `
    --query "[?roleDefinitionName=='IoT Hub Registry Contributor'].id | [0]" --output tsv --only-show-errors
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the Hub registry role assignment.'
}
if ($existing) {
    Write-Host "Hub registry role assignment already exists for $OperatorObjectId."
    return
}

if ($PSCmdlet.ShouldProcess("$OperatorObjectId at $HubResourceId", "Grant $role")) {
    & az role assignment create --assignee-object-id $OperatorObjectId --assignee-principal-type $PrincipalType `
        --role $role --scope $HubResourceId --output none --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to create the Hub registry role assignment.'
    }
    Write-Host "Granted $role at the Hub scope. Azure RBAC propagation may take several minutes."
}
