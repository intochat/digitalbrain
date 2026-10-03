param(
    [Parameter(Mandatory)][string]$Feed,
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$Source
)
$ErrorActionPreference = 'Stop'
$Feed = (Resolve-Path -LiteralPath $Feed).Path
$verified = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if (!$verified.Packages -or !$verified.VerifiedAt) { throw 'Invalid release verification manifest.' }
$files = @(Get-ChildItem -LiteralPath $Feed -Filter '*.nupkg')
if ($files.Count -ne $verified.Packages.Count) { throw 'Package set differs from the verified feed.' }
foreach ($package in $verified.Packages) {
    if ([IO.Path]::GetFileName($package.File) -cne $package.File) { throw 'Manifest contains an invalid package filename.' }
    $path = Join-Path $Feed $package.File
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $package.Sha256) {
        throw "Package changed after verification: $($package.File)"
    }
}
if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) { throw 'Set NUGET_API_KEY before publishing.' }
foreach ($package in $verified.Packages) {
    dotnet nuget push (Join-Path $Feed $package.File) --source $Source --api-key $env:NUGET_API_KEY --skip-duplicate
    if ($LASTEXITCODE -ne 0) { throw "Publishing failed: $($package.File)" }
}
