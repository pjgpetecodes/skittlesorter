#requires -Version 7.0

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$Location,

    [Parameter(Mandatory = $true)]
    [string]$NamespaceName,

    [Parameter(Mandatory = $true)]
    [string]$HubName,

    [Parameter(Mandatory = $true)]
    [string]$DpsName,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$')]
    [string]$EnrollmentId,

    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,127}$')]
    [string]$RegistrationId = 'skittlesorter-002',

    [string]$AppConfigPath = 'appsettings.002.json',

    [string]$BootstrapDirectory = 'certs\x509-bootstrap',

    [string]$RootCaName = 'root-ca',

    [string]$IntermediateCaName = 'intermediate-ca',

    [string]$PolicyName = 'default',

    [string]$RegistryOperatorObjectId,

    [ValidateSet('User', 'ServicePrincipal', 'Group')]
    [string]$RegistryOperatorPrincipalType = 'User',

    [ValidateRange(1, 90)]
    [int]$CertificateValidityDays = 30
)

$ErrorActionPreference = 'Stop'
$supportedRegions = @('australiaeast', 'japaneast', 'centralus', 'uksouth')
$normalizedLocation = $Location.Trim().ToLowerInvariant()
if ($normalizedLocation -notin $supportedRegions) {
    throw "Unsupported preview region '$Location'. Use one of: $($supportedRegions -join ', ')."
}
$Location = $normalizedLocation
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resolvedAppConfigPath = if ([System.IO.Path]::IsPathRooted($AppConfigPath)) {
    [System.IO.Path]::GetFullPath($AppConfigPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $AppConfigPath))
}
$writeAppConfig = -not (Test-Path -LiteralPath $resolvedAppConfigPath)
if (-not $writeAppConfig) {
    $existingConfig = Get-Content -LiteralPath $resolvedAppConfigPath -Raw | ConvertFrom-Json
    $existingDps = $existingConfig.IoTHub.DpsProvisioning
    if ($existingDps.AttestationMethod -ne 'X509' -or
        $existingDps.RequestOperationalCertificate -ne $true -or
        $existingDps.RegistrationId -ne $RegistrationId) {
        throw "Existing config '$resolvedAppConfigPath' is not the requested X.509 CSR profile. Keep it and choose a new -AppConfigPath and enrollment group."
    }
}

function Invoke-AzCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $result = & az @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output = ($result | ForEach-Object { "$_" }) -join [Environment]::NewLine

    if ($exitCode -ne 0) {
        throw "Azure CLI command failed with exit code ${exitCode}: az $($Arguments -join ' ')`n$output"
    }

    return $output.Trim()
}

function Invoke-AzCliStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host $Description -ForegroundColor Cyan
    $null = Invoke-AzCli -Arguments $Arguments
}

function Get-AzCliValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host $Description -ForegroundColor Cyan
    $value = Invoke-AzCli -Arguments ($Arguments + @('--only-show-errors'))
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Azure CLI returned an empty value while $Description"
    }

    return $value
}

function Invoke-AzCliCreateIfMissing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [string[]]$CheckArguments,

        [Parameter(Mandatory = $true)]
        [string[]]$CreateArguments,

        [string[]]$LocationArguments
    )

    $existingResource = Invoke-AzCli -Arguments ($CheckArguments + @('--only-show-errors'))
    if (-not [string]::IsNullOrWhiteSpace($existingResource) -and
        $existingResource.Trim().ToLowerInvariant() -notin @('false', 'null', 'none')) {
        if ($LocationArguments) {
            $existingLocation = Get-AzCliValue -Description "Checking location for $Description" -Arguments $LocationArguments
            if ($existingLocation.Trim().ToLowerInvariant() -ne $Location) {
                throw "$Description already exists in '$existingLocation', not the requested preview region '$Location'."
            }
        }

        Write-Host "$Description already exists; keeping it." -ForegroundColor Gray
        return
    }

    Invoke-AzCliStep -Description $Description -Arguments $CreateArguments
}

if (-not $PSCmdlet.ShouldProcess(
    "subscription '$SubscriptionId', resource group '$ResourceGroupName'",
    'Create ADR certificate management, an S1 IoT Hub with shared access policies enabled, DPS, an enrollment group, and any requested Hub registry role assignment; enable shared access on an existing Hub'
)) {
    return
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required. Install it before running this script.'
}

Write-Host 'This setup uses preview Azure Device Registry certificate management. IoT Hub S1 and DPS are billed at standard rates.' -ForegroundColor Yellow
Write-Host 'Hub shared access policies will be enabled, including on an existing Hub. Protect the Hub policy keys.' -ForegroundColor Yellow
Write-Host "Using supported preview region: $Location" -ForegroundColor Yellow
Write-Host 'No device records will be created. The enrollment group is a provisioning template only.' -ForegroundColor Yellow
if (-not $writeAppConfig) {
    Write-Host "App config already exists and will not be overwritten: $resolvedAppConfigPath" -ForegroundColor Yellow
}

$null = Get-AzCliValue -Description 'Checking Azure sign-in' -Arguments @(
    'account', 'show', '--query', 'id', '--output', 'tsv'
)
Invoke-AzCliStep -Description 'Selecting subscription' -Arguments @(
    'account', 'set', '--subscription', $SubscriptionId
)

$activeSubscriptionId = Get-AzCliValue -Description 'Verifying selected subscription' -Arguments @(
    'account', 'show', '--query', 'id', '--output', 'tsv'
)
if ($activeSubscriptionId -ne $SubscriptionId) {
    throw "Active subscription '$activeSubscriptionId' does not match '$SubscriptionId'."
}

Invoke-AzCliStep -Description 'Installing or upgrading the Azure IoT preview CLI extension' -Arguments @(
    'extension', 'add', '--name', 'azure-iot', '--allow-preview', 'true', '--upgrade', '--yes'
)

Invoke-AzCliStep -Description 'Registering Microsoft.DeviceRegistry resource provider' -Arguments @(
    'provider', 'register', '--namespace', 'Microsoft.DeviceRegistry', '--wait'
)
Invoke-AzCliStep -Description 'Registering Microsoft.Devices resource provider' -Arguments @(
    'provider', 'register', '--namespace', 'Microsoft.Devices', '--wait'
)

Invoke-AzCliCreateIfMissing -Description 'Creating resource group' `
    -CheckArguments @(
        'group', 'exists',
        '--name', $ResourceGroupName,
        '--output', 'tsv'
    ) `
    -LocationArguments @(
        'group', 'show',
        '--name', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', 'location',
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'group', 'create',
    '--name', $ResourceGroupName,
    '--location', $Location,
    '--subscription', $SubscriptionId,
    '--output', 'none'
)

Invoke-AzCliCreateIfMissing -Description 'Creating Device Registry namespace' `
    -CheckArguments @(
        'resource', 'list',
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$NamespaceName' && type=='Microsoft.DeviceRegistry/namespaces'].name | [0]",
        '--output', 'tsv'
    ) `
    -LocationArguments @(
        'iot', 'adr', 'ns', 'show',
        '--name', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', 'location',
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'create',
    '--name', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--location', $Location,
    '--outbound-system-assigned-mi', 'true',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)

Invoke-AzCliCreateIfMissing -Description 'Creating Microsoft-managed root CA' `
    -CheckArguments @(
        'iot', 'adr', 'ns', 'ca', 'list',
        '--namespace', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$RootCaName'].name | [0]",
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'ca', 'create',
    '--name', $RootCaName,
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--type', 'Root',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)

Invoke-AzCliCreateIfMissing -Description 'Creating Microsoft-issued intermediate CA' `
    -CheckArguments @(
        'iot', 'adr', 'ns', 'ca', 'list',
        '--namespace', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$IntermediateCaName'].name | [0]",
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'ca', 'create',
    '--name', $IntermediateCaName,
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--type', 'ICA',
    '--issuer-type', 'Microsoft',
    '--issuer-ca-name', $RootCaName,
    '--subscription', $SubscriptionId,
    '--output', 'none'
)

Invoke-AzCliCreateIfMissing -Description "Creating certificate policy '$PolicyName'" `
    -CheckArguments @(
        'iot', 'adr', 'ns', 'ca', 'policy', 'list',
        '--ca-name', $IntermediateCaName,
        '--namespace', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$PolicyName'].name | [0]",
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'ca', 'policy', 'create',
    '--name', $PolicyName,
    '--ca-name', $IntermediateCaName,
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--validity-days', "$CertificateValidityDays",
    '--subscription', $SubscriptionId,
    '--output', 'none'
)

Invoke-AzCliCreateIfMissing -Description 'Creating S1 DPS with system-assigned identity' `
    -CheckArguments @(
        'resource', 'list',
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$DpsName' && type=='Microsoft.Devices/ProvisioningServices'].name | [0]",
        '--output', 'tsv'
    ) `
    -LocationArguments @(
        'iot', 'dps', 'show',
        '--name', $DpsName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', 'location',
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'dps', 'create',
    '--name', $DpsName,
    '--resource-group', $ResourceGroupName,
    '--location', $Location,
    '--system-assigned-mi', 'true',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)
$dpsResourceId = Get-AzCliValue -Description 'Getting DPS resource ID' -Arguments @(
    'iot', 'dps', 'show',
    '--name', $DpsName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--query', 'id',
    '--output', 'tsv'
)

Invoke-AzCliCreateIfMissing -Description 'Linking DPS to the Device Registry namespace' `
    -CheckArguments @(
        'iot', 'adr', 'ns', 'link', 'dps', 'list',
        '--namespace', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='primary-dps'].name | [0]",
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'link', 'dps', 'add',
    '--name', 'primary-dps',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--dps-id', $dpsResourceId,
    '--system-assigned-mi',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)
$linkedDpsId = Get-AzCliValue -Description 'Checking the linked DPS target' -Arguments @(
    'iot', 'adr', 'ns', 'link', 'dps', 'list',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--query', "[?name=='primary-dps'].resourceId | [0]",
    '--output', 'tsv'
)
if ($linkedDpsId -ine $dpsResourceId) {
    throw "Namespace link 'primary-dps' targets '$linkedDpsId', not the requested DPS '$dpsResourceId'."
}

Invoke-AzCliCreateIfMissing -Description 'Creating S1 IoT Hub with system-assigned identity' `
    -CheckArguments @(
        'resource', 'list',
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='$HubName' && type=='Microsoft.Devices/IotHubs'].name | [0]",
        '--output', 'tsv'
    ) `
    -LocationArguments @(
        'iot', 'hub', 'show',
        '--name', $HubName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', 'location',
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'hub', 'create',
    '--name', $HubName,
    '--resource-group', $ResourceGroupName,
    '--location', $Location,
    '--sku', 'S1',
    '--system-assigned-mi', 'true',
    '--disable-local-auth', 'false',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)
$hubAuthArguments = @(
    'iot', 'hub', 'show',
    '--name', $HubName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--query', 'properties.disableLocalAuth',
    '--output', 'tsv'
)
$localAuthDisabled = (Get-AzCliValue -Description 'Checking IoT Hub shared access policies' -Arguments $hubAuthArguments).ToLowerInvariant()
if ($localAuthDisabled -eq 'true') {
    Invoke-AzCliStep -Description 'Enabling IoT Hub shared access policies' -Arguments @(
        'iot', 'hub', 'update',
        '--name', $HubName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--disable-local-auth', 'false',
        '--output', 'none'
    )
    $localAuthDisabled = (Get-AzCliValue -Description 'Verifying IoT Hub shared access policies' -Arguments $hubAuthArguments).ToLowerInvariant()
}
if ($localAuthDisabled -ne 'false') {
    throw "IoT Hub shared access policies could not be verified as enabled (disableLocalAuth: '$localAuthDisabled'). Check Azure Policy and Hub permissions."
}
$hubResourceId = Get-AzCliValue -Description 'Getting IoT Hub resource ID' -Arguments @(
    'iot', 'hub', 'show',
    '--name', $HubName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--query', 'id',
    '--output', 'tsv'
)

if ($RegistryOperatorObjectId) {
    & (Join-Path $PSScriptRoot 'grant-registry-access.ps1') `
        -HubResourceId $hubResourceId `
        -OperatorObjectId $RegistryOperatorObjectId `
        -PrincipalType $RegistryOperatorPrincipalType
}

Invoke-AzCliCreateIfMissing -Description 'Linking IoT Hub to the Device Registry namespace' `
    -CheckArguments @(
        'iot', 'adr', 'ns', 'link', 'hub', 'list',
        '--namespace', $NamespaceName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', "[?name=='primary-hub'].name | [0]",
        '--output', 'tsv'
    ) `
    -CreateArguments @(
    'iot', 'adr', 'ns', 'link', 'hub', 'add',
    '--name', 'primary-hub',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--hub-id', $hubResourceId,
    '--system-assigned-mi',
    '--subscription', $SubscriptionId,
    '--output', 'none'
)
$linkedHubId = Get-AzCliValue -Description 'Checking the linked IoT Hub target' -Arguments @(
    'iot', 'adr', 'ns', 'link', 'hub', 'list',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--query', "[?name=='primary-hub'].resourceId | [0]",
    '--output', 'tsv'
)
if ($linkedHubId -ine $hubResourceId) {
    throw "Namespace link 'primary-hub' targets '$linkedHubId', not the requested IoT Hub '$hubResourceId'."
}

Invoke-AzCliStep -Description 'Waiting for DPS and IoT Hub namespace links' -Arguments @(
    'iot', 'adr', 'ns', 'link', 'wait',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--dps-endpoint-name', 'primary-dps',
    '--hub-endpoint-name', 'primary-hub',
    '--subscription', $SubscriptionId
)

if ($writeAppConfig) {
    $idScope = Get-AzCliValue -Description 'Getting DPS ID scope for the local sorter config' -Arguments @(
        'iot', 'dps', 'show',
        '--name', $DpsName,
        '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId,
        '--query', 'properties.idScope',
        '--output', 'tsv'
    )
    $templatePath = Join-Path $PSScriptRoot '..\..\src\configuration\appsettings.template.json'
    if (-not (Test-Path -LiteralPath $templatePath)) {
        throw "Application settings template was not found: $templatePath"
    }

    $appConfig = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json
    $appConfig.IoTHub.DeviceConnectionString = ''
    $appConfig.IoTHub.DeviceId = '{RegistrationId}'
    $appConfig.IoTHub.SendTelemetry = $true
    $appConfig.IoTHub.DpsProvisioning.IdScope = $idScope
    $appConfig.IoTHub.DpsProvisioning.RegistrationId = $RegistrationId
    $appConfig.IoTHub.DpsProvisioning.AttestationMethod = 'X509'
    $appConfig.IoTHub.DpsProvisioning.EnrollmentGroupKeyBase64 = ''
    $appConfig.IoTHub.DpsProvisioning.DeviceKeyBase64 = ''
    $appConfig.IoTHub.DpsProvisioning.RequestOperationalCertificate = $true
    $appConfig.IoTHub.DpsProvisioning.AutoGenerateCsr = $true
    $appConfig.IoTHub.DpsProvisioning.EnableDebugLogging = $false
    $appConfig.Adr.Enabled = $false
    $appConfig.Hardware.MockMode.EnableMockColorSensor = $true
    $appConfig.Hardware.MockMode.EnableMockServos = $true

    $configDirectory = Split-Path -Parent $resolvedAppConfigPath
    if ($configDirectory) {
        New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    }
    $sourceConfig = [IO.Path]::GetTempFileName()
    try {
        $appConfig | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $sourceConfig -Encoding utf8NoBOM
        & (Join-Path $PSScriptRoot 'setup-x509-demo.ps1') `
            -SubscriptionId $SubscriptionId -ResourceGroupName $ResourceGroupName `
            -DpsName $DpsName -NamespaceName $NamespaceName -EnrollmentId $EnrollmentId `
            -GenerateDemoIssuer -SourceConfigPath $sourceConfig -OutputConfigPath $resolvedAppConfigPath `
            -DeviceDirectory (Join-Path (Join-Path $BootstrapDirectory $EnrollmentId) 'device') `
            -DemoIssuerDirectory (Join-Path (Join-Path $BootstrapDirectory $EnrollmentId) 'ca') `
            -IssuerDpsCertificateName "$EnrollmentId-bootstrap-ca" `
            -CertificateAuthorityName $IntermediateCaName -CertificatePolicyName $PolicyName `
            -RequestOperationalCertificate $true -Confirm:$false
    } finally {
        Remove-Item -LiteralPath $sourceConfig -ErrorAction SilentlyContinue
    }
    Write-Host 'Keep the bootstrap CA and device private keys private; the generated CA is for demonstrations only.' -ForegroundColor Yellow
} else {
    $enrollmentJson = Get-AzCliValue -Description 'Checking the existing DPS enrollment attestation and policy' -Arguments @(
        'iot', 'dps', 'enrollment-group', 'show', '--dps-name', $DpsName,
        '--resource-group', $ResourceGroupName, '--subscription', $SubscriptionId,
        '--enrollment-id', $EnrollmentId,
        '--query', '{type:attestation.type,thumb:attestation.x509.signingCertificates.primary.info.sha1Thumbprint,namespace:namespaceName,ca:certificateAuthorityName,policy:certificatePolicyName}',
        '--output', 'json'
    )
    $enrollment = $enrollmentJson | ConvertFrom-Json
    $idScope = Get-AzCliValue -Description 'Checking DPS ID scope' -Arguments @(
        'iot', 'dps', 'show', '--name', $DpsName, '--resource-group', $ResourceGroupName,
        '--subscription', $SubscriptionId, '--query', 'properties.idScope', '--output', 'tsv'
    )
    $chainPath = $existingDps.AttestationCertChainPath
    if (-not [IO.Path]::IsPathRooted($chainPath)) { $chainPath = Join-Path $repositoryRoot $chainPath }
    $chain = [Security.Cryptography.X509Certificates.X509Certificate2Collection]::new()
    try {
        $chain.ImportFromPemFile($chainPath)
        if ($chain.Count -eq 0 -or $enrollment.type -ne 'x509' -or
            $enrollment.thumb -ne $chain[0].Thumbprint -or
            $enrollment.namespace -ne $NamespaceName -or $enrollment.ca -ne $IntermediateCaName -or
            $enrollment.policy -ne $PolicyName -or $existingDps.IdScope -ne $idScope) {
            throw "Existing config and enrollment '$EnrollmentId' do not match the requested X.509 issuer, DPS scope and ADR policy."
        }
    } finally {
        foreach ($certificate in $chain) { $certificate.Dispose() }
    }
}

Write-Host 'Verifying connected services and certificate authorities:' -ForegroundColor Cyan
$dpsLinks = Invoke-AzCli -Arguments @(
    'iot', 'adr', 'ns', 'link', 'dps', 'list',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--output', 'table'
)
$hubLinks = Invoke-AzCli -Arguments @(
    'iot', 'adr', 'ns', 'link', 'hub', 'list',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--output', 'table'
)
$certificateAuthorities = Invoke-AzCli -Arguments @(
    'iot', 'adr', 'ns', 'ca', 'list',
    '--namespace', $NamespaceName,
    '--resource-group', $ResourceGroupName,
    '--subscription', $SubscriptionId,
    '--output', 'table'
)
if ($dpsLinks) { Write-Host $dpsLinks }
if ($hubLinks) { Write-Host $hubLinks }
if ($certificateAuthorities) { Write-Host $certificateAuthorities }

Write-Host ''
Write-Host 'Device Registry setup completed. No device records were created.' -ForegroundColor Green
Write-Host "Resource group: $ResourceGroupName"
Write-Host "Namespace:     $NamespaceName"
Write-Host "IoT Hub:       $HubName (S1)"
Write-Host "DPS:           $DpsName (S1)"
Write-Host "Enrollment:    $EnrollmentId"
Write-Host "Registration:  $RegistrationId (provisioned when the sorter runs, if not already present)"
if ($writeAppConfig) {
    Write-Host "App config:    $resolvedAppConfigPath"
    Write-Host "Run sorter:    dotnet run --project `"$repositoryRoot\skittlesorter.csproj`" -- --config `"$resolvedAppConfigPath`""
}
