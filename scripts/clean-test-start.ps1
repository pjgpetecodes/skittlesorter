#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppConfigPath,
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,
    [Parameter(Mandatory = $true)]
    [string]$DpsName,
    [Parameter(Mandatory = $true)]
    [string]$IotHubName,
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,
    [Parameter(Mandatory = $true)]
    [string]$EnrollmentGroupName,
    [Parameter(Mandatory = $true)]
    [string]$DeviceId,
    [Parameter(Mandatory = $true)]
    [string]$AdrNamespace,
    [ValidateSet('login', 'key')]
    [string]$HubAuthType = 'login'
)

$ErrorActionPreference = 'Stop'
$configPath = (Resolve-Path -LiteralPath $AppConfigPath).Path
$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$registration = $configuration.IoTHub.DpsProvisioning.RegistrationId
if (-not $registration -or $registration -ne $DeviceId) {
    throw 'DeviceId must exactly match the configuration RegistrationId.'
}

$target = "$SubscriptionId/$ResourceGroup/$DpsName/$IotHubName/$AdrNamespace/$DeviceId"
if (-not $PSCmdlet.ShouldProcess($target, 'Reset the exact demo device and retain enrollment, CA, and local identity files')) {
    return
}

$resetScript = Join-Path $PSScriptRoot 'iot-hub-device-registry\test-unified.ps1'
$resetParameters = @{
    AppConfigPath = $configPath
    ResetDevice = $true
    ResetOnly = $true
    SubscriptionId = $SubscriptionId
    ResourceGroupName = $ResourceGroup
    DpsName = $DpsName
    EnrollmentId = $EnrollmentGroupName
    HubName = $IotHubName
    NamespaceName = $AdrNamespace
    HubAuthType = $HubAuthType
}
$previousConfirmPreference = $ConfirmPreference
try {
    $ConfirmPreference = 'None'
    & $resetScript @resetParameters
} finally {
    $ConfirmPreference = $previousConfirmPreference
}
