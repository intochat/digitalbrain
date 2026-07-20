[CmdletBinding()]
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$samples = @('samples/DigitalBrain.Quickstart', 'samples/DigitalBrain.Multiagent')
$repositoryPath = [System.IO.Path]::GetFullPath($repository)
$tempPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$cache = [System.IO.Path]::GetFullPath(
    (Join-Path $tempPath "digitalbrain-empty-cache-$([guid]::NewGuid().ToString('n'))"))
$previous = $env:NUGET_PACKAGES

function Assert-DescendantPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($Root)
    $rootPrefix = $fullRoot

    if (-not $rootPrefix.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPrefix += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $fullPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "path must stay inside '$fullRoot': $fullPath"
    }

    $fullPath
}

try {
    $cache = Assert-DescendantPath -Path $cache -Root $tempPath
    $env:NUGET_PACKAGES = $cache

    foreach ($sample in $samples) {
        $project = Assert-DescendantPath -Path (Join-Path $repositoryPath $sample) -Root $repositoryPath

        foreach ($directory in @('obj', 'bin')) {
            $buildOutput = Assert-DescendantPath -Path (Join-Path $project $directory) -Root $project
            Remove-Item -Recurse -Force -LiteralPath $buildOutput -ErrorAction SilentlyContinue
        }

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
    $cache = Assert-DescendantPath -Path $cache -Root $tempPath
    Remove-Item -Recurse -Force -LiteralPath $cache -ErrorAction SilentlyContinue
}
