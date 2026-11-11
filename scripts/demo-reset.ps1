#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('CloudOnly', 'OperationalIdentity', 'FullBootstrap')]
    [string]$Scenario,
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
if ($target.AppConfigPath -ne 'appsettings.x509-demo.json') {
    throw 'Demo reset is restricted to appsettings.x509-demo.json.'
}
if ($target.DeviceId -notmatch '^[a-z0-9][a-z0-9-]{0,127}$') {
    throw 'DeviceId must be a valid lowercase DPS registration ID.'
}
if ($target.EnrollmentId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') {
    throw 'EnrollmentId contains characters that are not valid for the dedicated demo enrollment.'
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

function Resolve-ConfiguredPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $expanded = $Path.Replace('{RegistrationId}', $target.DeviceId, [StringComparison]::OrdinalIgnoreCase)
    $resolved = Resolve-RepositoryPath $expanded
    $certsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'certs'))
    $relative = [IO.Path]::GetRelativePath($certsRoot, $resolved)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or
        $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw "Configured identity paths must stay inside the repository certs directory: $Path"
    }
    return $resolved
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    $currentPath = [IO.Path]::GetFullPath($Path)
    while ($currentPath) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Demo reset refuses to follow a link or junction: $currentPath"
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
$deviceDirectory = Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'device'
$issuerDirectory = Join-Path (Join-Path $bootstrapRoot $target.EnrollmentId) 'ca'
$configPath = Resolve-RepositoryPath $target.AppConfigPath
Assert-NoReparsePoint $configPath
if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "The dedicated demo config is missing. Run the demo setup first: $configPath"
}
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$dps = $config.IoTHub.DpsProvisioning
if ($dps.RegistrationId -ne $target.DeviceId -or $dps.IdScope -ne $target.DpsIdScope -or
    $dps.AttestationMethod -ne 'X509' -or $dps.RequestOperationalCertificate -ne $true -or
    $dps.AutoGenerateCsr -ne $true) {
    throw 'The dedicated app config must match the target registration, DPS scope, and X.509 CSR profile.'
}

$expectedPaths = @{
    AttestationCertPath = Join-Path $deviceDirectory "$($target.DeviceId)-bootstrap.pem"
    AttestationKeyPath = Join-Path $deviceDirectory "$($target.DeviceId)-bootstrap.key"
    AttestationCertChainPath = Join-Path $issuerDirectory 'bootstrap-chain.pem'
    CsrFilePath = Join-Path (Join-Path $deviceDirectory 'operational') "$($target.DeviceId).csr"
    CsrKeyFilePath = Join-Path (Join-Path $deviceDirectory 'operational') "$($target.DeviceId).key"
    IssuedCertFilePath = Join-Path (Join-Path $deviceDirectory 'operational') "$($target.DeviceId)-issued.pem"
}
foreach ($property in $expectedPaths.Keys) {
    $configured = $dps.$property
    if ([string]::IsNullOrWhiteSpace($configured) -or
        (Resolve-ConfiguredPath $configured) -ne [IO.Path]::GetFullPath($expectedPaths[$property])) {
        throw "The dedicated app config path '$property' does not match the demo target."
    }
}

$operationalFiles = @(
    (Resolve-ConfiguredPath $dps.CsrFilePath),
    (Resolve-ConfiguredPath $dps.CsrKeyFilePath),
    (Resolve-ConfiguredPath $dps.IssuedCertFilePath),
    ((Resolve-ConfiguredPath $dps.IssuedCertFilePath) + '.connection.json')
)
$localFiles = @($operationalFiles[-1])
if ($Scenario -in @('OperationalIdentity', 'FullBootstrap')) {
    $localFiles += $operationalFiles[0..2]
}
if ($Scenario -eq 'FullBootstrap') {
    $localFiles += @(
        (Resolve-ConfiguredPath $dps.AttestationCertPath),
        (Resolve-ConfiguredPath $dps.AttestationKeyPath),
        (Resolve-ConfiguredPath $dps.AttestationCertChainPath),
        (Join-Path $issuerDirectory 'bootstrap-intermediate.pem'),
        (Join-Path $issuerDirectory 'bootstrap-intermediate.key'),
        (Join-Path $issuerDirectory 'bootstrap-root.pem'),
        (Join-Path $issuerDirectory 'bootstrap-root.key'),
        $configPath
    )
}
$localFiles = @($localFiles | Select-Object -Unique)
foreach ($path in $localFiles) {
    Assert-NoReparsePoint $path
}
$scope = "$($target.SubscriptionId)/$($target.ResourceGroupName)/$($target.DpsName)/$($target.HubName)/$($target.NamespaceName)/$($target.DeviceId)"
$action = switch ($Scenario) {
    'CloudOnly' { 'Delete only the exact ADR, Hub and DPS device records and cached assignment' }
    'OperationalIdentity' { 'Delete exact device records, assignment, CSR/key and issued operational certificate; retain bootstrap identity and enrollment' }
    'FullBootstrap' { 'Delete exact device records, demo enrollment/verified issuer and local demo identity/config; retain Azure infrastructure and shared ADR CA/policy' }
}

if ($WhatIfPreference) {
    $null = $PSCmdlet.ShouldProcess($scope, $action)
    Write-Host "Local file targets for ${Scenario}:"
    foreach ($path in $localFiles) {
        $state = if (Test-Path -LiteralPath $path -PathType Leaf) { 'present' } else { 'absent' }
        Write-Host "  [$state] $path"
    }
    if ($Scenario -eq 'FullBootstrap') {
        Write-Host "  Azure targets: device '$($target.DeviceId)', enrollment '$($target.EnrollmentId)', DPS certificate '$($target.IssuerDpsCertificateName)'"
        Write-Host '  Retained: resource group, IoT Hub, DPS, ADR namespace, shared ADR CA and policy'
    } else {
        Write-Host "  Azure targets: exact device '$($target.DeviceId)' in ADR, Hub and DPS"
    }
    return
}

if ($Scenario -eq 'FullBootstrap') {
    $issuerPath = Join-Path $issuerDirectory 'bootstrap-intermediate.pem'
    if (-not (Test-Path -LiteralPath $issuerPath -PathType Leaf)) {
        throw "Cannot verify the dedicated DPS bootstrap issuer: $issuerPath"
    }
    $issuer = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($issuerPath)
    try {
        $certificateResult = & az iot dps certificate show `
            --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
            --name $target.IssuerDpsCertificateName --subscription $target.SubscriptionId `
            --query '{etag:etag,thumbprint:properties.thumbprint,verified:properties.isVerified}' `
            --only-show-errors --output json
        if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the exact demo DPS certificate; no reset was run.' }
        $certificate = ($certificateResult -join "`n") | ConvertFrom-Json
        if (-not $certificate.etag -or -not $certificate.verified -or
            $certificate.thumbprint -ne $issuer.Thumbprint) {
            throw 'The named DPS certificate is not the verified issuer in the dedicated demo directory.'
        }
    } finally {
        $issuer.Dispose()
    }
    $enrollmentResult = & az iot dps enrollment-group show `
        --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
        --enrollment-id $target.EnrollmentId --subscription $target.SubscriptionId `
        --query '{id:enrollmentGroupId,namespace:namespaceName,ca:certificateAuthorityName,policy:certificatePolicyName}' `
        --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the exact demo enrollment; no reset was run.' }
    $enrollment = ($enrollmentResult -join "`n") | ConvertFrom-Json
    if ($enrollment.id -ne $target.EnrollmentId -or
        $enrollment.namespace -ne $target.NamespaceName -or
        $enrollment.ca -ne $target.CertificateAuthorityName -or
        $enrollment.policy -ne $target.CertificatePolicyName) {
        throw 'The named enrollment does not match the dedicated demo namespace, CA and policy.'
    }
    $registrationResult = & az iot dps enrollment-group registration list `
        --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
        --enrollment-id $target.EnrollmentId --subscription $target.SubscriptionId `
        --query '[].registrationId' --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect registrations in the demo enrollment; no reset was run.' }
    $registrations = @($registrationResult -join "`n" | ConvertFrom-Json)
    if ($registrations.Count -ne 1 -or $registrations[0] -ne $target.DeviceId) {
        throw 'Full bootstrap reset requires this dedicated enrollment to contain only the configured demo device.'
    }
}

if (-not $PSCmdlet.ShouldProcess($scope, $action)) { return }

$resetScript = Join-Path $PSScriptRoot 'iot-hub-device-registry\test-unified.ps1'
$resetParameters = @{
    AppConfigPath = $configPath
    ResetDevice = $true
    ResetOnly = $true
    SubscriptionId = $target.SubscriptionId
    ResourceGroupName = $target.ResourceGroupName
    DpsName = $target.DpsName
    EnrollmentId = $target.EnrollmentId
    HubName = $target.HubName
    NamespaceName = $target.NamespaceName
}
$previousConfirmPreference = $ConfirmPreference
try {
    $ConfirmPreference = 'None'
    & $resetScript @resetParameters
} finally {
    $ConfirmPreference = $previousConfirmPreference
}

if ($Scenario -eq 'FullBootstrap') {
    $null = & az iot dps enrollment-group delete `
        --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
        --enrollment-id $target.EnrollmentId --subscription $target.SubscriptionId `
        --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Device records were reset, but deleting the exact demo enrollment failed.' }

    $etag = & az iot dps certificate show `
        --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
        --name $target.IssuerDpsCertificateName --subscription $target.SubscriptionId `
        --query etag --only-show-errors --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($etag -join ''))) {
        throw 'Device records and enrollment were reset, but the verified demo certificate etag could not be read.'
    }
    $null = & az iot dps certificate delete `
        --dps-name $target.DpsName --resource-group $target.ResourceGroupName `
        --name $target.IssuerDpsCertificateName --etag ($etag -join '') `
        --subscription $target.SubscriptionId --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw 'Device records and enrollment were reset, but deleting the exact demo certificate failed.' }
}

foreach ($path in $localFiles) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}

Write-Host "Demo reset '$Scenario' completed for '$($target.DeviceId)'."
if ($Scenario -eq 'FullBootstrap') {
    Write-Host 'Run scripts\demo-setup.ps1 to recreate the demo bootstrap issuer, enrollment and config before F5.'
}
