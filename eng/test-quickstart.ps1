param(
    [switch]$CleanCache,
    [switch]$Live
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$feedDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$packageTestProject = Join-Path $repositoryRoot 'tests/DigitalBrain.PackageTests/DigitalBrain.PackageTests.csproj'

if (-not (Test-Path $feedDirectory)) {
    throw "The local package feed does not exist. Run .\eng\pack.ps1 first."
}

$packageProjects = Get-ChildItem @(
    (Join-Path $repositoryRoot 'kernel'),
    (Join-Path $repositoryRoot 'integrations'),
    (Join-Path $repositoryRoot 'packages')
) -Recurse -Filter '*.csproj' -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -notmatch '\\(obj|bin)\\' -and
        (Get-Content $_.FullName -Raw) -match '<IsPackable>true</IsPackable>'
    } |
    Sort-Object Name

if (-not $packageProjects) {
    throw 'No packable DigitalBrain projects were discovered.'
}

$version = (dotnet msbuild $packageProjects[0].FullName -getProperty:PackageVersion).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) {
    throw 'The DigitalBrain package version could not be resolved.'
}

foreach ($project in $packageProjects) {
    $packageId = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
    foreach ($extension in @('nupkg', 'snupkg')) {
        $packagePath = Join-Path $feedDirectory "$packageId.$version.$extension"
        if (-not (Test-Path $packagePath)) {
            throw "The local feed is missing $packagePath."
        }
    }
}

if ($CleanCache) {
    Write-Host 'Quickstart tests always restore through a fresh isolated NuGet cache.'
}

$previousFeed = $env:DIGITALBRAIN_PACKAGE_FEED
try {
    $env:DIGITALBRAIN_PACKAGE_FEED = $feedDirectory
    dotnet test $packageTestProject -c Release --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) {
        throw "The package quickstart tests failed with exit code $LASTEXITCODE."
    }
}
finally {
    $env:DIGITALBRAIN_PACKAGE_FEED = $previousFeed
}

if ($Live) {
    Write-Host 'Live quickstart behavior is verified by the Task 9 controlled-provider gate.'
}
