# Pack the eleven DigitalBrain packages as <VERSION>-preview.<N> into a flat local feed (outside every repository).
#
# Usage: scripts/pack-local.ps1 -Preview 1 [-Feed E:/intochat/local-feed]
# Bump the preview number after every digitalbrain change; consumers pin the exact version.
param(
    [Parameter(Mandatory = $true)][int]$Preview,
    [string]$Feed = 'E:/intochat/local-feed'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$version = "$((Get-Content (Join-Path $root 'VERSION') -Raw).Trim())-preview.$Preview"
New-Item -ItemType Directory -Force -Path $Feed | Out-Null

$ids = 'DigitalBrain', 'DigitalBrain.UI', 'DigitalBrain.Testing', 'DigitalBrain.Analyzers', 'DigitalBrain.Templates',
    'DigitalBrain.Runtime.Orleans', 'DigitalBrain.Runtime.Orleans.Azure', 'DigitalBrain.Capabilities.Storage', 'DigitalBrain.Capabilities.Secrets', 'DigitalBrain.Capabilities.Identity', 'DigitalBrain.Runtime.Http'
foreach ($id in $ids) {
    if (Test-Path (Join-Path $Feed "$id.$version.nupkg")) { throw "$id.$version.nupkg exists in $Feed; bump the preview number" }
}

dotnet pack (Join-Path $root 'DigitalBrain.slnx') -c Release -o $Feed "-p:VersionSuffix=preview.$Preview"
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack DigitalBrain.slnx failed' }
dotnet pack (Join-Path $root 'templates/DigitalBrain.Templates/DigitalBrain.Templates.csproj') -c Release -o $Feed "-p:VersionSuffix=preview.$Preview"
if ($LASTEXITCODE -ne 0) { throw 'dotnet pack DigitalBrain.Templates failed' }

Write-Host "packed $version into ${Feed}:"
Get-ChildItem $Feed -Filter "*.$version.*nupkg" | ForEach-Object { Write-Host "  $($_.Name)" }
