#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$TargetPath = (Join-Path $PSScriptRoot 'demo-target.local.json')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$target = Get-Content -LiteralPath (Resolve-Path -LiteralPath $TargetPath) -Raw | ConvertFrom-Json

foreach ($name in 'SubscriptionId', 'ResourceGroupName', 'DpsName', 'HubName',
        'NamespaceName', 'EnrollmentId', 'DeviceId', 'DpsIdScope',
        'BootstrapDirectory', 'CertificateAuthorityName', 'CertificatePolicyName',
        'IssuerDpsCertificateName') {
    $value = $target.$name
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[<>]') {
        throw "The local demo target must define '$name' with a real value."
    }
}

if ($target.DeviceId -notmatch '^[a-z0-9][a-z0-9-]{0,127}$') {
    throw 'DeviceId must be a valid lowercase DPS registration ID.'
}
if ($target.EnrollmentId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') {
    throw 'EnrollmentId contains characters that are not valid for the dedicated demo enrollment.'
}
if ($target.AppConfigPath -ne 'appsettings.x509-demo.json') {
    throw 'The demo setup is restricted to appsettings.x509-demo.json.'
}

function Resolve-RepositoryPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    } else {
        [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
    }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $fullPath)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw "Path must stay inside the repository: $Path"
    }
    return $fullPath
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    $currentPath = [IO.Path]::GetFullPath($Path)
    while ($currentPath) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Demo setup refuses to follow a link or junction: $currentPath"
            }
        }
        if ($currentPath -eq $repositoryRoot) { break }
        $parent = Split-Path -Parent $currentPath
        if (-not $parent -or $parent -eq $currentPath) { break }
        $currentPath = $parent
    }
}

$bootstrapRoot = Resolve-RepositoryPath $target.BootstrapDirectory
$expectedBootstrapRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'certs\x509-demo'))
if ($bootstrapRoot -ne $expectedBootstrapRoot) {
    throw 'BootstrapDirectory must be the dedicated repository certs\x509-demo directory.'
}
Assert-NoReparsePoint $bootstrapRoot
Assert-NoReparsePoint (Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'device')
Assert-NoReparsePoint (Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'ca')
Assert-NoReparsePoint (Resolve-RepositoryPath $target.AppConfigPath)

$templatePath = Join-Path $repositoryRoot 'src\configuration\appsettings.template.json'
$appConfig = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
$appConfig.IoTHub.DeviceId = '{RegistrationId}'
$appConfig.IoTHub.DpsProvisioning.IdScope = $target.DpsIdScope
$appConfig.IoTHub.DpsProvisioning.RegistrationId = $target.DeviceId
$appConfig.IoTHub.DpsProvisioning.AttestationMethod = 'X509'
$appConfig.IoTHub.DpsProvisioning.RequestOperationalCertificate = $true
$appConfig.IoTHub.DpsProvisioning.AutoGenerateCsr = $true
$appConfig.IoTHub.DpsProvisioning.EnrollmentGroupKeyBase64 = ''
$appConfig.IoTHub.DpsProvisioning.DeviceKeyBase64 = ''
$appConfig.Adr.Enabled = $false
$sourceConfig = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N') + '.json')
$previousWhatIfPreference = $WhatIfPreference
try {
    $WhatIfPreference = $false
    $appConfig | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $sourceConfig -Encoding utf8NoBOM
    $setupScript = Join-Path $PSScriptRoot 'iot-hub-device-registry\setup-x509-demo.ps1'
    $parameters = @{
        SubscriptionId = $target.SubscriptionId
        ResourceGroupName = $target.ResourceGroupName
        DpsName = $target.DpsName
        NamespaceName = $target.NamespaceName
        EnrollmentId = $target.EnrollmentId
        GenerateDemoIssuer = $true
        SourceConfigPath = $sourceConfig
        OutputConfigPath = (Resolve-RepositoryPath $target.AppConfigPath)
        DeviceDirectory = (Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'device')
        DemoIssuerDirectory = (Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'ca')
        CertificateAuthorityName = $target.CertificateAuthorityName
        CertificatePolicyName = $target.CertificatePolicyName
        IssuerDpsCertificateName = $target.IssuerDpsCertificateName
        RequestOperationalCertificate = $true
    }
    $commonParameters = @{}
    if ($previousWhatIfPreference) { $commonParameters.WhatIf = $true }
    if ($PSBoundParameters.ContainsKey('Confirm')) {
        $commonParameters.Confirm = [bool]$PSBoundParameters['Confirm']
    }
    & $setupScript @parameters @commonParameters
} finally {
    $WhatIfPreference = $false
    if (Test-Path -LiteralPath $sourceConfig) {
        Remove-Item -LiteralPath $sourceConfig -Force
    }
    $WhatIfPreference = $previousWhatIfPreference
}
