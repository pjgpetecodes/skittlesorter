[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet("linux-arm", "linux-arm64")][string]$Runtime,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [bool]$SessionCounts = $true,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\..\..\artifacts\updates")
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publish = Join-Path $output "$Runtime-$Version-publish"
if (Test-Path -LiteralPath $publish) {
    throw "Publish directory already exists. Use a new output directory to avoid packaging stale files: $publish"
}
dotnet publish (Join-Path $root "skittlesorter.csproj") --configuration Release `
    --runtime $Runtime --self-contained true --output $publish `
    "-p:Version=$Version" "-p:SessionCountsEnabled=$($SessionCounts.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }
python (Join-Path $PSScriptRoot "package-release.py") --publish $publish --output $output `
    --version $Version --runtime $Runtime --session-counts $SessionCounts.ToString().ToLowerInvariant()
if ($LASTEXITCODE -ne 0) { throw "Release packaging failed." }
