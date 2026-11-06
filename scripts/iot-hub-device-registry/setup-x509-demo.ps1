#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string]$SubscriptionId,
    [Parameter(Mandatory = $true)][string]$ResourceGroupName,
    [Parameter(Mandatory = $true)][string]$DpsName,
    [Parameter(Mandatory = $true)][string]$NamespaceName,
    [Parameter(Mandatory = $true)][string]$EnrollmentId,
    [Parameter(Mandatory = $true)][string]$IssuerCertificatePath,
    [Parameter(Mandatory = $true)][string]$IssuerKeyPath,
    [Parameter(Mandatory = $true)][string]$IssuerChainPath,
    [string]$SourceConfigPath = 'appsettings.002.json',
    [string]$OutputConfigPath = 'appsettings.x509-002.json',
    [string]$DeviceDirectory = 'certs\x509-demo',
    [string]$CertificateAuthorityName = 'intermediate-ca',
    [string]$CertificatePolicyName = 'default',
    [string]$IssuerDpsCertificateName = 'skittlesorter-x509-demo-intermediate'
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
function Resolve-LocalPath([string]$path) {
    if ([IO.Path]::IsPathRooted($path)) { return [IO.Path]::GetFullPath($path) }
    return [IO.Path]::GetFullPath((Join-Path $root $path))
}
function Invoke-Azure([string[]]$arguments) {
    $result = & az @arguments --subscription $SubscriptionId --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($arguments -join ' ')" }
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}

$source = Resolve-LocalPath $SourceConfigPath
$output = Resolve-LocalPath $OutputConfigPath
$issuerPath = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerCertificatePath)).Path
$issuerKey = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerKeyPath)).Path
$chainPath = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerChainPath)).Path
$config = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$dps = $config.IoTHub.DpsProvisioning
if (-not $dps.IdScope -or $dps.RegistrationId -notmatch '^[a-z0-9][a-z0-9-]{0,127}$') {
    throw 'The source config must contain a DPS ID scope and a valid lowercase registration ID.'
}
$registration = $dps.RegistrationId
$directory = Resolve-LocalPath $DeviceDirectory
$certPath = Join-Path $directory "$registration-bootstrap.pem"
$keyPath = Join-Path $directory "$registration-bootstrap.key"
foreach ($path in @($output, $certPath, $keyPath)) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite existing file: $path" }
}
if (-not $PSCmdlet.ShouldProcess("$DpsName/$EnrollmentId and $output",
    'Trust the supplied issuer as a verified DPS CA and create an X.509 enrollment and local bootstrap credentials')) { return }

$scope = Invoke-Azure @('iot', 'dps', 'show', '-g', $ResourceGroupName, '-n', $DpsName, '--query', 'properties.idScope')
if ($scope -ne $dps.IdScope) { throw 'The source configuration ID scope does not match the target DPS.' }
$groups = @(Invoke-Azure @('iot', 'dps', 'enrollment-group', 'list', '-g', $ResourceGroupName,
    '--dps-name', $DpsName, '--query', '[].enrollmentGroupId'))
if ($EnrollmentId -in $groups) { throw "Enrollment group '$EnrollmentId' already exists; inspect it before proceeding." }

$issuer = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPemFile($issuerPath, $issuerKey)
$rsa = [Security.Cryptography.RSA]::Create(2048)
try {
    $chain = [Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    $chain.ImportFromPemFile($chainPath)
    try {
        if ($chain.Count -eq 0 -or $chain[0].Thumbprint -ne $issuer.Thumbprint) {
            throw 'The issuer chain must start with the supplied issuer certificate.'
        }
    } finally { foreach ($item in $chain) { $item.Dispose() } }
    $start = [DateTimeOffset]::UtcNow.AddMinutes(-5)
    $end = [DateTimeOffset]::UtcNow.AddDays(365)
    if ($start -lt $issuer.NotBefore.ToUniversalTime() -or $end -ge $issuer.NotAfter.ToUniversalTime()) {
        throw 'Issuer validity does not cover the requested bootstrap certificate lifetime.'
    }
    $certificates = Invoke-Azure @('iot', 'dps', 'certificate', 'list', '-g', $ResourceGroupName, '--dps-name', $DpsName)
    $existingCa = @($certificates.value | Where-Object { $_.name -eq $IssuerDpsCertificateName })
    if ($existingCa.Count -gt 0) {
        if (-not $existingCa[0].properties.isVerified -or
            $existingCa[0].properties.thumbprint -ne $issuer.Thumbprint) {
            throw "DPS CA '$IssuerDpsCertificateName' is unverified or does not match the supplied issuer."
        }
    } else {
        $null = Invoke-Azure @('iot', 'dps', 'certificate', 'create', '-g', $ResourceGroupName,
            '--dps-name', $DpsName, '--certificate-name', $IssuerDpsCertificateName,
            '--path', $issuerPath, '--verified', 'true')
    }
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        "CN=$registration", $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $usage = [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor
        [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($usage, $true))
    $oids = [Security.Cryptography.OidCollection]::new()
    $null = $oids.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.2'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oids, $false))
    $serial = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $serial[0] = $serial[0] -band 0x7f
    $certificate = $request.Create($issuer, $start, $end, $serial)
    try {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        [IO.File]::WriteAllText($keyPath, $rsa.ExportPkcs8PrivateKeyPem())
        [IO.File]::WriteAllText($certPath, $certificate.ExportCertificatePem())
    } finally { $certificate.Dispose() }
} finally {
    $rsa.Dispose()
    $issuer.Dispose()
}

$null = Invoke-Azure @('iot', 'dps', 'enrollment-group', 'create', '-g', $ResourceGroupName,
    '--dps-name', $DpsName, '--enrollment-id', $EnrollmentId, '--certificate-path', $issuerPath,
    '--adr-namespace', $NamespaceName, '--adr-ca-name', $CertificateAuthorityName,
    '--adr-cert-policy-name', $CertificatePolicyName)
$dps.AttestationMethod = 'X509'
$dps.AttestationCertPath = $certPath
$dps.AttestationKeyPath = $keyPath
$dps.AttestationCertChainPath = $chainPath
$dps.RequestOperationalCertificate = $false
$dps.AutoGenerateCsr = $false
if ($dps.PSObject.Properties['DeviceKeyBase64']) { $dps.DeviceKeyBase64 = '' }
if ($dps.PSObject.Properties['EnrollmentGroupKeyBase64']) { $dps.EnrollmentGroupKeyBase64 = '' }
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $output -Encoding utf8NoBOM
Write-Host "Created X.509 enrollment '$EnrollmentId' and private local config '$output'."
