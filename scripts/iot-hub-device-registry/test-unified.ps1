#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$AppConfigPath,
    [switch]$ResetDevice,
    [string]$SubscriptionId,
    [string]$ResourceGroupName,
    [string]$DpsName,
    [string]$EnrollmentId,
    [string]$HubName,
    [string]$NamespaceName,
    [switch]$ResetOnly
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$configPath = (Resolve-Path -LiteralPath $AppConfigPath).Path
$configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$dps = $configuration.IoTHub.DpsProvisioning
if (-not $dps.IdScope -or -not $dps.RegistrationId) {
    throw 'The selected configuration must contain a DPS ID scope and registration ID.'
}
$registration = $dps.RegistrationId
if ($ResetOnly -and -not $ResetDevice) {
    throw '-ResetOnly requires -ResetDevice.'
}

function Invoke-Azure {
    param([string[]]$Arguments)
    $result = & az @Arguments --subscription $SubscriptionId --only-show-errors --output json
    if ($LASTEXITCODE -ne 0) { throw "Azure command failed: az $($Arguments -join ' ')" }
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}

function Test-HubDevice {
    $result = & az iot hub device-identity show -g $ResourceGroupName --hub-name $HubName `
        --device-id $registration --auth-type login --subscription $SubscriptionId `
        --query 'deviceId' --only-show-errors --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        $message = ($result | ForEach-Object { "$_" }) -join "`n"
        if ($message -match 'ErrorCode:DeviceNotFound;404') { return $false }
        throw "Unable to inspect the exact Hub identity: $message"
    }
    if (($result -join "`n" | ConvertFrom-Json) -ne $registration) {
        throw 'Hub identity does not match the exact reset target.'
    }
    return $true
}

if ($ResetDevice) {
    foreach ($required in 'SubscriptionId', 'ResourceGroupName', 'DpsName', 'EnrollmentId', 'HubName', 'NamespaceName') {
        if (-not (Get-Variable -Name $required -ValueOnly)) { throw "-$required is required with -ResetDevice." }
    }
    $service = Invoke-Azure @('iot', 'dps', 'show', '-g', $ResourceGroupName, '-n', $DpsName, '--query', 'properties.idScope')
    if ($service -ne $dps.IdScope) { throw 'The selected configuration ID scope does not match the specified DPS service.' }
    $enrollment = Invoke-Azure @('iot', 'dps', 'enrollment-group', 'show', '-g', $ResourceGroupName,
        '--dps-name', $DpsName, '--enrollment-id', $EnrollmentId,
        '--query', '{id:enrollmentGroupId,namespace:namespaceName}')
    if ($enrollment.namespace -ne $NamespaceName) { throw 'The enrollment group does not reference the specified ADR namespace.' }
    $registrations = @(Invoke-Azure @('iot', 'dps', 'enrollment-group', 'registration', 'list', '-g', $ResourceGroupName,
        '--dps-name', $DpsName, '--enrollment-id', $EnrollmentId, '--query', '[].registrationId'))
    if ($registration -notin $registrations) {
        throw 'The configured device is not registered in the specified enrollment group; select the group used for the previous run.'
    }
    $hub = Invoke-Azure @('iot', 'hub', 'show', '-g', $ResourceGroupName, '-n', $HubName, '--query', 'name')
    if ($hub -ne $HubName) { throw 'The specified IoT Hub was not found.' }
    $state = Invoke-Azure @('iot', 'dps', 'enrollment-group', 'registration', 'show', '-g', $ResourceGroupName,
        '--dps-name', $DpsName, '--registration-id', $registration,
        '--query', '{id:registrationId,device:deviceId,hub:assignedHub}')
    if ($state.id -ne $registration -or $state.device -ne $registration -or
        $state.hub -notin @("$HubName.azure-devices.net", "$HubName.device.azure-devices.net")) {
        throw 'DPS registration/device/Hub assignment does not match the exact reset target.'
    }
    $devices = @(Invoke-Azure @('iot', 'adr', 'ns', 'device', 'list', '-g', $ResourceGroupName,
        '--namespace', $NamespaceName, '--query', '[].name'))
    $hubDeviceExists = Test-HubDevice
    if (-not $hubDeviceExists -and $registration -in $devices) {
        throw 'The Hub identity is missing while the ADR device still exists; investigate before resetting.'
    }
}

if (-not $ResetOnly) {
    if (-not $PSCmdlet.ShouldProcess($registration, 'Build and run a two-minute, hardware-free unified SDK cloud test')) { return }
    & dotnet build (Join-Path $repositoryRoot 'skittlesorter.csproj') --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Sorter build failed; no device records have been deleted.' }
}

if ($ResetDevice) {
    if (-not $PSCmdlet.ShouldProcess("$ResourceGroupName/$NamespaceName/$registration",
        'Delete this exact ADR device, Hub identity and DPS registration; retain enrollment, CAs and all resources')) { return }

    if ($registration -in $devices) {
        $null = Invoke-Azure @('iot', 'adr', 'ns', 'device', 'delete', '-g', $ResourceGroupName,
            '--namespace', $NamespaceName, '-n', $registration, '--yes')
        $null = Invoke-Azure @('iot', 'adr', 'ns', 'device', 'wait', '-g', $ResourceGroupName,
            '--namespace', $NamespaceName, '-n', $registration, '--deleted', '--timeout', '120')
    }
    if (Test-HubDevice) {
        $null = Invoke-Azure @('iot', 'hub', 'device-identity', 'delete', '-g', $ResourceGroupName,
            '--hub-name', $HubName, '--device-id', $registration, '--auth-type', 'login')
    }
    $null = Invoke-Azure @('iot', 'dps', 'enrollment-group', 'registration', 'delete', '-g', $ResourceGroupName,
        '--dps-name', $DpsName, '--registration-id', $registration)

    # Only discard the cached assignment. Keep the existing CSR, key and certificate.
    # DpsConfiguration resolves relative paths against the nearest project root, otherwise the config directory.
    $base = Split-Path -Parent $configPath
    $ancestor = Get-Item -LiteralPath $base
    while ($ancestor) {
        if (Get-ChildItem -LiteralPath $ancestor.FullName -Filter '*.csproj' -File | Select-Object -First 1) {
            $base = $ancestor.FullName
            break
        }
        $ancestor = $ancestor.Parent
    }
    $issued = $dps.IssuedCertFilePath
    if (-not $issued) { $issued = 'certs/issued/issued.pem' }
    $issued = $issued.Replace('{RegistrationId}', $registration, [StringComparison]::OrdinalIgnoreCase)
    $issued = $issued.Replace('\', [IO.Path]::DirectorySeparatorChar).Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not [IO.Path]::IsPathRooted($issued)) { $issued = Join-Path $base $issued }
    $assignment = [IO.Path]::GetFullPath($issued) + '.connection.json'
    if (Test-Path -LiteralPath $assignment) { Remove-Item -LiteralPath $assignment }
}

if ($ResetOnly) {
    Write-Host "Clean demo start prepared for '$registration'. Enrollment, CA, CSR, key, and certificate files were retained."
    return
}

& dotnet run --no-build --project (Join-Path $repositoryRoot 'skittlesorter.csproj') -- --cloud-self-test --config $configPath
if ($LASTEXITCODE -ne 0) { throw 'Unified SDK cloud self-test failed; inspect the error above. No success is recorded.' }

if ($ResetDevice) {
    $result = Invoke-Azure @('iot', 'dps', 'enrollment-group', 'registration', 'show', '-g', $ResourceGroupName,
        '--dps-name', $DpsName, '--registration-id', $registration,
        '--query', '{status:status,device:deviceId,hub:assignedHub}')
    if ($result.status -ne 'assigned' -or $result.device -ne $registration) {
        throw 'The cloud test returned but the exact device was not assigned in DPS.'
    }
    $device = Invoke-Azure @('iot', 'adr', 'ns', 'device', 'show', '-g', $ResourceGroupName,
        '--namespace', $NamespaceName, '-n', $registration, '--query', 'properties.externalDeviceId')
    if ($device -ne $registration) { throw 'The ADR device was not recreated with the expected external ID.' }
    Write-Host "Verified fresh DPS assignment and ADR device: $registration"
}
