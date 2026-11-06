$ErrorActionPreference = "Stop"

function Assert-UpdateCli {
    $extension = az extension show --name azure-iot --query version --output tsv --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "Install the azure-iot extension explicitly before running this script."
    }
    Write-Host "azure-iot extension: $extension. These draft scripts target 1.0.0b2 (6 October 2026)."
}

function Invoke-UpdateAzure {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $result = & az @Arguments --subscription $Subscription --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "Azure command failed: $($Arguments[0..([Math]::Min(2, $Arguments.Length - 1))] -join ' ')" }
    return $result
}

function Assert-NewUpdateResourceGroup {
    $exists = Invoke-UpdateAzure -Arguments @("group", "exists", "--name", $ResourceGroup, "-o", "tsv")
    if ($exists -ne "false") {
        throw "Refusing to reuse an existing resource group. Choose a new dedicated update lab resource group."
    }
}

function Assert-UpdateHubSharedAccess {
    $disabled = Invoke-UpdateAzure -Arguments @("iot", "hub", "show", "-n", $Hub, "-g", $ResourceGroup, "--query", "properties.disableLocalAuth", "-o", "tsv")
    if ("$disabled".Trim().ToLowerInvariant() -ne "false") {
        throw "IoT Hub shared access policies are not verified as enabled (disableLocalAuth: '$disabled'). Check Azure Policy and Hub permissions."
    }
}

function Read-ApplicationImportManifest {
    param([string]$Path, [string]$Handler)
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $data = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    if ($data.manifestVersion -ne "5.0" -or @($data.instructions.steps).Count -ne 1 -or $data.instructions.steps[0].handler -ne $Handler) {
        throw "Expected one schema-5 inline step for handler '$Handler'."
    }
    if ($data.instructions.steps[0].type -ne "inline") { throw "Reference steps are not supported." }
    foreach ($file in $data.files) {
        if ([string]::IsNullOrWhiteSpace($file.filename) -or $file.filename -match '[/\\]' -or $file.filename -in @(".", "..")) {
            throw "Payload paths must be basenames."
        }
        $payload = Join-Path (Split-Path $resolved) $file.filename
        $item = Get-Item -LiteralPath $payload
        $hash = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash))
        if ($item.Length -ne $file.sizeInBytes -or $hash -cne $file.hashes.sha256) {
            throw "Payload size/hash differs from the manifest: $($file.filename)"
        }
    }
    return $resolved
}
