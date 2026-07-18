param(
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$feedDirectory = Join-Path $repositoryRoot 'artifacts/packages'

if (Test-Path $feedDirectory) {
    Remove-Item -Recurse -Force $feedDirectory
}
New-Item -ItemType Directory -Force $feedDirectory | Out-Null

$searchRoots = @('kernel', 'integrations', 'packages') |
    ForEach-Object { Join-Path $repositoryRoot $_ } |
    Where-Object { Test-Path $_ }
$packageProjects = Get-ChildItem $searchRoots -Recurse -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and (Get-Content $_.FullName -Raw) -match '<IsPackable>true</IsPackable>' } |
    Sort-Object Name

if (-not $packageProjects) {
    throw 'No packable DigitalBrain projects were discovered.'
}

foreach ($project in $packageProjects) {
    if ($Clean) {
        dotnet clean $project.FullName -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet clean failed for $($project.Name) with exit code $LASTEXITCODE."
        }
    }
    dotnet pack $project.FullName -c Release -o $feedDirectory -p:ContinuousIntegrationBuild=true --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $($project.Name) with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'package-metadata.ps1') -FeedDirectory $feedDirectory

Get-ChildItem $feedDirectory -Filter '*.nupkg' | ForEach-Object { $_.FullName }
Get-ChildItem $feedDirectory -Filter '*.snupkg' | ForEach-Object { $_.FullName }
