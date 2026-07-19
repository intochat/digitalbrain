[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$samples = @('samples/DigitalBrain.Quickstart', 'samples/DigitalBrain.Multiagent')
$cache = Join-Path ([System.IO.Path]::GetTempPath()) "digitalbrain-empty-cache-$([guid]::NewGuid().ToString('n'))"
$previous = $env:NUGET_PACKAGES

try {
    $env:NUGET_PACKAGES = $cache

    foreach ($sample in $samples) {
        $project = Join-Path $repository $sample

        Remove-Item -Recurse -Force (Join-Path $project 'obj'), (Join-Path $project 'bin') -ErrorAction SilentlyContinue

        Push-Location $project

        try {
            dotnet run -c $Configuration
            if ($LASTEXITCODE -ne 0) { throw "$sample failed against an empty package cache with exit code $LASTEXITCODE" }
        }
        finally {
            Pop-Location
        }
    }

    "both samples restored, built and ran from the local feed against an empty package cache"
}
finally {
    $env:NUGET_PACKAGES = $previous
    Remove-Item -Recurse -Force $cache -ErrorAction SilentlyContinue
}
