#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string]$SubscriptionId,
    [Parameter(Mandatory = $true)][string]$ResourceGroupName,
    [Parameter(Mandatory = $true)][string]$DpsName,
    [Parameter(Mandatory = $true)][string]$NamespaceName,
    [Parameter(Mandatory = $true)][string]$EnrollmentId,
    [string]$IssuerCertificatePath,
    [string]$IssuerKeyPath,
    [string]$IssuerChainPath,
    [switch]$GenerateDemoIssuer,
    [string]$DemoIssuerDirectory = 'certs\x509-demo',
    [string]$SourceConfigPath = 'appsettings.002.json',
    [string]$OutputConfigPath = 'appsettings.x509-002.json',
    [string]$DeviceDirectory = 'certs\x509-demo',
    [string]$CertificateAuthorityName = 'intermediate-ca',
    [string]$CertificatePolicyName = 'default',
    [string]$IssuerDpsCertificateName = 'skittlesorter-x509-demo-intermediate',
    [bool]$RequestOperationalCertificate = $true
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
function Write-PrivateKey([string]$path, [string]$pem) {
    $options = [IO.FileStreamOptions]::new()
    $options.Mode = [IO.FileMode]::CreateNew
    $options.Access = [IO.FileAccess]::Write
    if (-not $IsWindows) {
        $options.UnixCreateMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite
    }
    $stream = [IO.File]::Open($path, $options)
    $writer = [IO.StreamWriter]::new($stream)
    try { $writer.Write($pem) } finally { $writer.Dispose() }
}

$source = Resolve-LocalPath $SourceConfigPath
$output = Resolve-LocalPath $OutputConfigPath
if ($GenerateDemoIssuer) {
    if ($IssuerCertificatePath -or $IssuerKeyPath -or $IssuerChainPath) {
        throw 'Supply either -GenerateDemoIssuer or an existing issuer certificate, key and chain, not both.'
    }
    $issuerDirectory = Resolve-LocalPath $DemoIssuerDirectory
    $issuerPath = Join-Path $issuerDirectory 'bootstrap-intermediate.pem'
    $issuerKey = Join-Path $issuerDirectory 'bootstrap-intermediate.key'
    $chainPath = Join-Path $issuerDirectory 'bootstrap-chain.pem'
    $rootPath = Join-Path $issuerDirectory 'bootstrap-root.pem'
    $rootKeyPath = Join-Path $issuerDirectory 'bootstrap-root.key'
} else {
    if (-not $IssuerCertificatePath -or -not $IssuerKeyPath -or -not $IssuerChainPath) {
        throw 'Provide an issuer certificate, private key and chain, or use -GenerateDemoIssuer.'
    }
    $issuerPath = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerCertificatePath)).Path
    $issuerKey = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerKeyPath)).Path
    $chainPath = (Resolve-Path -LiteralPath (Resolve-LocalPath $IssuerChainPath)).Path
}
$config = Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$dps = $config.IoTHub.DpsProvisioning
if (-not $dps.IdScope -or $dps.RegistrationId -notmatch '^[a-z0-9][a-z0-9-]{0,127}$') {
    throw 'The source config must contain a DPS ID scope and a valid lowercase registration ID.'
}
$registration = $dps.RegistrationId
$directory = Resolve-LocalPath $DeviceDirectory
$certPath = Join-Path $directory "$registration-bootstrap.pem"
$keyPath = Join-Path $directory "$registration-bootstrap.key"
$operationalDirectory = Join-Path $directory 'operational'
$csrPath = Join-Path $operationalDirectory "$registration.csr"
$csrKeyPath = Join-Path $operationalDirectory "$registration.key"
$issuedPath = Join-Path $operationalDirectory "$registration-issued.pem"
$newPaths = @($output, $certPath, $keyPath)
if ($RequestOperationalCertificate) {
    $newPaths += @($csrPath, $csrKeyPath, $issuedPath, "$issuedPath.connection.json")
}
foreach ($path in $newPaths) {
    if (Test-Path -LiteralPath $path) { throw "Refusing to overwrite existing file: $path" }
}
if (-not $PSCmdlet.ShouldProcess("$DpsName/$EnrollmentId and $output",
    'Trust the supplied issuer as a verified DPS CA and create an X.509 enrollment and local bootstrap credentials')) { return }

$scope = Invoke-Azure @('iot', 'dps', 'show', '-g', $ResourceGroupName, '-n', $DpsName, '--query', 'properties.idScope')
if ($scope -ne $dps.IdScope) { throw 'The source configuration ID scope does not match the target DPS.' }
$groups = @(Invoke-Azure @('iot', 'dps', 'enrollment-group', 'list', '-g', $ResourceGroupName,
    '--dps-name', $DpsName, '--query', '[].enrollmentGroupId'))
if ($EnrollmentId -in $groups) { throw "Enrollment group '$EnrollmentId' already exists; inspect it before proceeding." }

if ($GenerateDemoIssuer) {
    $issuerFiles = @($issuerPath, $issuerKey, $chainPath, $rootPath, $rootKeyPath)
    $existingIssuerFiles = @($issuerFiles | Where-Object { Test-Path -LiteralPath $_ })
    if ($existingIssuerFiles.Count -gt 0 -and $existingIssuerFiles.Count -ne $issuerFiles.Count) {
        throw "Demo bootstrap CA files are incomplete in $(Split-Path $issuerPath); inspect them before retrying."
    }
    if ($existingIssuerFiles.Count -eq 0) {
        New-Item -ItemType Directory -Force -Path (Split-Path $issuerPath) | Out-Null
        $rootRsa = [Security.Cryptography.RSA]::Create(3072)
        $issuerRsa = [Security.Cryptography.RSA]::Create(3072)
        try {
            $rootRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=SkittleSorter Demo Bootstrap Root', $rootRsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $rootRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 1, $true))
            $rootRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
            $rootCertificate = $rootRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-10), [DateTimeOffset]::UtcNow.AddYears(10))
            $issuerRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
                'CN=SkittleSorter Demo Bootstrap Intermediate', $issuerRsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $issuerRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 0, $true))
            $issuerRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign, $true))
            $serial = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
            $serial[0] = $serial[0] -band 0x7f
            $issuerCertificate = $issuerRequest.Create($rootCertificate, [DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(5), $serial)
            try {
                Write-PrivateKey $rootKeyPath ($rootRsa.ExportPkcs8PrivateKeyPem())
                Write-PrivateKey $issuerKey ($issuerRsa.ExportPkcs8PrivateKeyPem())
                [IO.File]::WriteAllText($rootPath, $rootCertificate.ExportCertificatePem())
                [IO.File]::WriteAllText($issuerPath, $issuerCertificate.ExportCertificatePem())
                [IO.File]::WriteAllText($chainPath, $issuerCertificate.ExportCertificatePem() + "`n" + $rootCertificate.ExportCertificatePem())
            } finally { $issuerCertificate.Dispose(); $rootCertificate.Dispose() }
        } finally { $issuerRsa.Dispose(); $rootRsa.Dispose() }
        Write-Warning 'Created a self-signed demo bootstrap CA. Protect its private key; do not use this CA in production.'
    }
}

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
        Write-PrivateKey $keyPath ($rsa.ExportPkcs8PrivateKeyPem())
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
$dps.RequestOperationalCertificate = $RequestOperationalCertificate
$dps.AutoGenerateCsr = $RequestOperationalCertificate
if ($RequestOperationalCertificate) {
    $dps | Add-Member -NotePropertyName CsrFilePath -NotePropertyValue $csrPath -Force
    $dps | Add-Member -NotePropertyName CsrKeyFilePath -NotePropertyValue $csrKeyPath -Force
    $dps | Add-Member -NotePropertyName IssuedCertFilePath -NotePropertyValue $issuedPath -Force
}
if ($dps.PSObject.Properties['DeviceKeyBase64']) { $dps.DeviceKeyBase64 = '' }
if ($dps.PSObject.Properties['EnrollmentGroupKeyBase64']) { $dps.EnrollmentGroupKeyBase64 = '' }
$config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $output -Encoding utf8NoBOM
Write-Host "Created X.509 enrollment '$EnrollmentId' and private local config '$output'."
