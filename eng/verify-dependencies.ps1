[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repository 'DigitalBrain.slnx'

Push-Location $repository

try {
    $findings = @()

    foreach ($check in @('--vulnerable', '--deprecated')) {
        $report = dotnet list $solution package $check --include-transitive 2>&1 | Out-String

        if ($LASTEXITCODE -ne 0) {
            $findings += "dotnet list package $check could not complete, so nothing was verified:`n$report"
            continue
        }

        if ($report -match '(?m)^\s+>\s') {
            $findings += "dotnet list package $check reported findings:`n$report"
        }
    }

    $centrally = Select-String -Path (Join-Path $repository 'Directory.Packages.props') -Pattern '<PackageVersion Include="([^"]+)" Version="([^"]+)"'
    $floating = $centrally | Where-Object { $_.Matches[0].Groups[2].Value -match '[\*\(\)\[\]]' }

    if ($floating) {
        $findings += "every pin must be an exact version: $(($floating | ForEach-Object { $_.Matches[0].Groups[1].Value }) -join ', ')"
    }

    if ($findings) {
        $findings | ForEach-Object { Write-Error $_ -ErrorAction Continue }
        throw 'dependency hygiene gate failed'
    }

    "no vulnerable or deprecated dependencies, and every version is an exact pin"
}
finally {
    Pop-Location
}
