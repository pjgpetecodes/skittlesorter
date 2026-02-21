#!/usr/bin/env pwsh

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroup = "pjgiothubdemo003",
    [string]$Location = "northeurope",
    [ValidateNotNullOrEmpty()]
    [string]$AdrNamespace = "pjgadrnamespace003",
    [ValidateNotNullOrEmpty()]
    [string]$UserIdentity = "pjgiothubdemoidentity003",
    [ValidateNotNullOrEmpty()]
    [string]$NewHubName = "pjgiothubdemo0032",
    [ValidateNotNullOrEmpty()]
    [string]$NewDpsName = "pjgiothubdemodps003",
    [ValidateNotNullOrEmpty()]
    [string]$EnrollmentId = "skittlesorter",
    [ValidateNotNullOrEmpty()]
    [string]$CaName = "skittlesorter-intermediate",
    [ValidateNotNullOrEmpty()]
    [string]$CredentialPolicyName = "cert-policy",
    [ValidateNotNullOrEmpty()]
    [string]$CaCertPath = ".\scripts\certs\ca\ca.pem",
    [ValidateNotNullOrEmpty()]
    [string]$CaKeyPath = ".\scripts\certs\ca\ca.key",
    [switch]$UpdateAppSettings,
    [string]$AppSettingsPath = ".\appsettings.json"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===`n" -ForegroundColor Cyan
}

function Invoke-AzCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [switch]$IgnoreError
    )

    Write-Host "> $Command" -ForegroundColor DarkGray
    $output = Invoke-Expression "$Command 2>&1"
    $exitCode = $LASTEXITCODE
    $outputText = ($output | Out-String).Trim()

    $outputText = (($outputText -split "`r?`n") | Where-Object {
            $_ -and $_ -notmatch '^WARNING:\s+The behavior of this command has been altered by the following extension: azure-iot$'
        }) -join "`n"
    $outputText = $outputText.Trim()

    if ($exitCode -ne 0 -and -not $IgnoreError) {
        throw "Command failed (exit $exitCode): $Command`n$outputText"
    }

    return $outputText
}

function Ensure-RoleAssignment {
    param(
        [string]$Assignee,
        [string]$Role,
        [string]$Scope
    )

    $output = Invoke-AzCommand "az role assignment create --assignee `"$Assignee`" --role `"$Role`" --scope `"$Scope`" --output none" -IgnoreError
    if ($LASTEXITCODE -ne 0 -and ($output -notmatch "RoleAssignmentExists|already exists")) {
        throw "Failed role assignment [$Role] on [$Scope]: $output"
    }
}

function Ensure-AdrPolicy {
    param(
        [string]$Namespace,
        [string]$ResourceGroup,
        [string]$PolicyName
    )

    $showOutput = Invoke-AzCommand "az iot adr ns policy show --namespace $Namespace --resource-group $ResourceGroup --name $PolicyName --output none" -IgnoreError
    if ($LASTEXITCODE -eq 0) {
        Write-Host "ADR policy '$PolicyName' already exists."
        return
    }

    $createAttempts = @(
        "az iot adr ns policy create --namespace $Namespace --resource-group $ResourceGroup --name $PolicyName --output none",
        "az iot adr ns policy create --namespace $Namespace --resource-group $ResourceGroup --policy-name $PolicyName --output none",
        "az iot adr ns policy create --namespace $Namespace --resource-group $ResourceGroup --credential-name default --name $PolicyName --output none"
    )

    foreach ($cmd in $createAttempts) {
        $output = Invoke-AzCommand $cmd -IgnoreError
        if ($LASTEXITCODE -eq 0 -or $output -match "already exists") {
            Write-Host "ADR policy '$PolicyName' ensured."
            return
        }
    }

    throw "Unable to create ADR policy '$PolicyName'. Check command shape for your azure-iot extension version."
}

function Invoke-AdrCredentialSyncWithRetry {
    param(
        [string]$Namespace,
        [string]$ResourceGroup,
        [int]$MaxAttempts = 1,
        [int]$DelaySeconds = 0
    )

    $output = Invoke-AzCommand "az iot adr ns credential sync --namespace $Namespace --resource-group $ResourceGroup -o json" -IgnoreError
    if ($LASTEXITCODE -eq 0) {
        Write-Host "ADR credential sync succeeded."
        return
    }

    Write-Host "ADR credential sync error:" -ForegroundColor Red
    Write-Host $output -ForegroundColor Red
    throw "ADR credential sync failed."
}

function Ensure-AdrEndpointHubPermissions {
    param(
        [string]$Namespace,
        [string]$ResourceGroup,
        [string]$AdrPrincipalId
    )

    $endpointIdsRaw = Invoke-AzCommand "az iot adr ns show --name $Namespace --resource-group $ResourceGroup --query `"properties.messaging.endpoints.*.resourceId`" -o tsv" -IgnoreError
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($endpointIdsRaw)) {
        Write-Warning "No ADR messaging endpoints found (or unable to query). Skipping endpoint permission grant."
        return
    }

    $endpointIds = $endpointIdsRaw -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($endpointId in $endpointIds) {
        $resolvedId = (Invoke-AzCommand "az resource show --ids `"$endpointId`" --query id -o tsv" -IgnoreError).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedId)) {
            Write-Warning "Skipping missing endpoint resource: $endpointId"
            continue
        }

        Ensure-RoleAssignment -Assignee $AdrPrincipalId -Role "Contributor" -Scope $resolvedId
        Ensure-RoleAssignment -Assignee $AdrPrincipalId -Role "IoT Hub Registry Contributor" -Scope $resolvedId
    }
}

Write-Step "Resolve ADR and Identity IDs"

$adrNsId = (Invoke-AzCommand "az iot adr ns show --name $AdrNamespace --resource-group $ResourceGroup --query id -o tsv").Trim()
if ([string]::IsNullOrWhiteSpace($adrNsId)) {
    throw "ADR namespace not found: $AdrNamespace"
}

$uamiId = (Invoke-AzCommand "az identity show --name $UserIdentity --resource-group $ResourceGroup --query id -o tsv" -IgnoreError).Trim()
if ([string]::IsNullOrWhiteSpace($uamiId)) {
    Write-Host "User-assigned identity '$UserIdentity' not found. Creating..."
    Invoke-AzCommand "az identity create --name $UserIdentity --resource-group $ResourceGroup --location $Location --output none" | Out-Null
    $uamiId = (Invoke-AzCommand "az identity show --name $UserIdentity --resource-group $ResourceGroup --query id -o tsv").Trim()
}

$adrPrincipalId = (Invoke-AzCommand "az iot adr ns show --name $AdrNamespace --resource-group $ResourceGroup --query identity.principalId -o tsv").Trim()
if ([string]::IsNullOrWhiteSpace($adrPrincipalId)) {
    throw "Could not resolve ADR namespace principalId."
}

Write-Step "Create new GEN2 IoT Hub linked to ADR"

$hubCreatePrimary = Invoke-AzCommand "az iot hub create --name $NewHubName --resource-group $ResourceGroup --location $Location --sku GEN2 --adr-ns-id $adrNsId --adr-identity-id $uamiId -o json" -IgnoreError
if ($LASTEXITCODE -ne 0) {
    $hubCreateFallback = Invoke-AzCommand "az iot hub create --name $NewHubName --resource-group $ResourceGroup --location $Location --sku GEN2 --ns-resource-id $adrNsId --ns-identity-id $uamiId -o json" -IgnoreError
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create GEN2 hub with ADR integration.`nPrimary:`n$hubCreatePrimary`nFallback:`n$hubCreateFallback"
    }
}

$newHubId = (Invoke-AzCommand "az iot hub show --name $NewHubName --resource-group $ResourceGroup --query id -o tsv").Trim()

Write-Step "Grant ADR namespace identity access to new hub"
Ensure-RoleAssignment -Assignee $adrPrincipalId -Role "Contributor" -Scope $newHubId
Ensure-RoleAssignment -Assignee $adrPrincipalId -Role "IoT Hub Registry Contributor" -Scope $newHubId

Write-Step "Create new DPS linked to ADR"

$existingDpsId = (Invoke-AzCommand "az iot dps show --name $NewDpsName --resource-group $ResourceGroup --query id -o tsv" -IgnoreError).Trim()
if (-not [string]::IsNullOrWhiteSpace($existingDpsId)) {
    Write-Host "DPS '$NewDpsName' already exists in resource group '$ResourceGroup'. Reusing it."
}
else {
    $dpsCreatePrimary = Invoke-AzCommand "az iot dps create --name $NewDpsName --resource-group $ResourceGroup --location $Location --mi-user-assigned $uamiId --ns-resource-id $adrNsId --ns-identity-id $uamiId -o json" -IgnoreError
    if ($LASTEXITCODE -ne 0) {
        $dpsCreateFallback = Invoke-AzCommand "az iot dps create --name $NewDpsName --resource-group $ResourceGroup --location $Location --ns-resource-id $adrNsId --ns-identity-id $uamiId -o json" -IgnoreError
        if ($LASTEXITCODE -ne 0) {
            if ($dpsCreatePrimary -match "IotDps name '.*' is not available|IH400307" -or $dpsCreateFallback -match "IotDps name '.*' is not available|IH400307") {
                $suffix = Get-Random -Minimum 1000 -Maximum 9999
                $candidateName = "$NewDpsName$suffix"
                if ($candidateName.Length -gt 64) {
                    $candidateName = $candidateName.Substring(0, 64)
                }

                Write-Warning "DPS name '$NewDpsName' is not available. Retrying with '$candidateName'."
                $NewDpsName = $candidateName

                $retryCreate = Invoke-AzCommand "az iot dps create --name $NewDpsName --resource-group $ResourceGroup --location $Location --mi-user-assigned $uamiId --ns-resource-id $adrNsId --ns-identity-id $uamiId -o json" -IgnoreError
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to create DPS with ADR integration after name retry.`nPrimary:`n$dpsCreatePrimary`nFallback:`n$dpsCreateFallback`nRetry:`n$retryCreate"
                }
            }
            else {
                throw "Failed to create DPS with ADR integration.`nPrimary:`n$dpsCreatePrimary`nFallback:`n$dpsCreateFallback"
            }
        }
    }
}

Write-Step "Link new DPS to new hub"
$linkOutput = Invoke-AzCommand "az iot dps linked-hub create --dps-name $NewDpsName --resource-group $ResourceGroup --hub-name $NewHubName -o json" -IgnoreError
if ($LASTEXITCODE -ne 0 -and ($linkOutput -notmatch "already exists|LinkedHubAlreadyExists|IH400303|duplicate iot hubs")) {
    throw "Failed to link DPS and IoT Hub: $linkOutput"
}

Write-Step "Ensure ADR credential and policy"

$credentialOutput = Invoke-AzCommand "az iot adr ns credential show --namespace $AdrNamespace --resource-group $ResourceGroup --output none" -IgnoreError
if ($LASTEXITCODE -ne 0) {
    Invoke-AzCommand "az iot adr ns credential create --namespace $AdrNamespace --resource-group $ResourceGroup --output none" | Out-Null
}

Ensure-AdrPolicy -Namespace $AdrNamespace -ResourceGroup $ResourceGroup -PolicyName $CredentialPolicyName

Write-Step "Ensure ADR identity has access to all ADR endpoint hubs"
Ensure-AdrEndpointHubPermissions -Namespace $AdrNamespace -ResourceGroup $ResourceGroup -AdrPrincipalId $adrPrincipalId

Write-Step "Sync ADR credential to linked hubs"
Invoke-AdrCredentialSyncWithRetry -Namespace $AdrNamespace -ResourceGroup $ResourceGroup

Write-Step "Ensure DPS CA certificate exists and is verified"

$certShowOutput = Invoke-AzCommand "az iot dps certificate show --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName -o json" -IgnoreError
if ($LASTEXITCODE -ne 0) {
    if (-not (Test-Path $CaCertPath)) {
        throw "CA certificate not found at path: $CaCertPath"
    }

    Invoke-AzCommand "az iot dps certificate create --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName --path $CaCertPath -o json" | Out-Null
    $certShowOutput = Invoke-AzCommand "az iot dps certificate show --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName -o json"
}

$certObject = $certShowOutput | ConvertFrom-Json
$isVerified = [bool]$certObject.properties.isVerified

if (-not $isVerified) {
    if (-not (Get-Command openssl -ErrorAction SilentlyContinue)) {
        throw "OpenSSL is required to verify DPS CA certificate but is not installed/in PATH. Install OpenSSL, then rerun."
    }

    if (-not (Test-Path $CaKeyPath)) {
        throw "CA private key not found at path: $CaKeyPath"
    }

    $verifyCsrPath = ".\scripts\certs\ca\dps-verify-$($NewDpsName).csr"
    $verifyPemPath = ".\scripts\certs\ca\dps-verify-$($NewDpsName).pem"

    $etag = (Invoke-AzCommand "az iot dps certificate show --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName --query etag -o tsv").Trim()
    $verifyCode = (Invoke-AzCommand "az iot dps certificate generate-verification-code --dps-name $NewDpsName --resource-group $ResourceGroup --certificate-name $CaName --etag $etag --query properties.verificationCode -o tsv").Trim()

    Invoke-AzCommand "openssl req -new -key $CaKeyPath -subj '/CN=$verifyCode' -out $verifyCsrPath" | Out-Null
    Invoke-AzCommand "openssl x509 -req -in $verifyCsrPath -CA $CaCertPath -CAkey $CaKeyPath -CAcreateserial -out $verifyPemPath -days 1 -sha256" | Out-Null

    $etag = (Invoke-AzCommand "az iot dps certificate show --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName --query etag -o tsv").Trim()
    Invoke-AzCommand "az iot dps certificate verify --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName --path $verifyPemPath --etag $etag -o json" | Out-Null

    $isVerifiedText = (Invoke-AzCommand "az iot dps certificate show --dps-name $NewDpsName --resource-group $ResourceGroup --name $CaName --query properties.isVerified -o tsv").Trim()
    if ($isVerifiedText -ne "true") {
        throw "DPS certificate verification did not complete successfully."
    }
}

Write-Step "Ensure enrollment group"

$enrollmentShow = Invoke-AzCommand "az iot dps enrollment-group show --dps-name $NewDpsName --resource-group $ResourceGroup --enrollment-id $EnrollmentId -o json" -IgnoreError
if ($LASTEXITCODE -ne 0) {
    Invoke-AzCommand "az iot dps enrollment-group create --dps-name $NewDpsName --resource-group $ResourceGroup --enrollment-id $EnrollmentId --ca-name $CaName --credential-policy $CredentialPolicyName --provisioning-status enabled -o json" | Out-Null
}

Write-Step "Output verification values"

$idScope = (Invoke-AzCommand "az iot dps show --name $NewDpsName --resource-group $ResourceGroup --query properties.idScope -o tsv").Trim()
Write-Host "IdScope: $idScope" -ForegroundColor Green

Invoke-AzCommand "az iot dps linked-hub list --dps-name $NewDpsName --resource-group $ResourceGroup -o table" | Out-Host
Invoke-AzCommand "az iot dps enrollment-group show --dps-name $NewDpsName --resource-group $ResourceGroup --enrollment-id $EnrollmentId -o json" | Out-Host

if ($UpdateAppSettings) {
    Write-Step "Update appsettings.json"

    if (-not (Test-Path $AppSettingsPath)) {
        throw "AppSettings file not found at path: $AppSettingsPath"
    }

    $appConfig = Get-Content $AppSettingsPath -Raw | ConvertFrom-Json

    $appConfig.IoTHub.DpsProvisioning.IdScope = $idScope
    $appConfig.IoTHub.DpsProvisioning.RegistrationId = $EnrollmentId
    $appConfig.Adr.ResourceGroupName = $ResourceGroup
    $appConfig.Adr.NamespaceName = $AdrNamespace

    $appConfig | ConvertTo-Json -Depth 25 | Set-Content $AppSettingsPath -Encoding UTF8
    Write-Host "Updated appsettings: $AppSettingsPath" -ForegroundColor Green
}

Write-Host "`nDone. Next: run the device app and monitor registration/telemetry on $NewHubName." -ForegroundColor Green
