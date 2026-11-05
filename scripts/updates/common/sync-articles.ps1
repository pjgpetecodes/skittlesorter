[CmdletBinding()]
param([Parameter(Mandatory)][string]$Exporter)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
if (!(Test-Path -LiteralPath $Exporter -PathType Leaf)) { throw "WordPress Markdown exporter not found." }
foreach ($series in @("device-update-for-iot-hub", "adr-software-updates")) {
    $source = Join-Path $root "blog-remote\$series"
    $draft = Join-Path $root "blog\$series"
    New-Item -ItemType Directory -Path $draft -Force | Out-Null
    foreach ($markdown in Get-ChildItem -LiteralPath $source -Filter "*.md") {
        # These new series have no remote IDs: export only, never sync/push to WordPress.
        node $Exporter export $markdown.FullName ([IO.Path]::ChangeExtension($markdown.FullName, ".html")) --force
        if ($LASTEXITCODE -ne 0) { throw "HTML export failed for $($markdown.Name)." }
        Copy-Item -LiteralPath $markdown.FullName -Destination (Join-Path $draft $markdown.Name) -Force
    }
}
