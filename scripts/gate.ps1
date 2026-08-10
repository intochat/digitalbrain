param(
    [switch]$Flutter,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$locks = Get-Process -Name 'DigitalBrain*' -ErrorAction SilentlyContinue
if ($locks) {
    Write-Host "GATE FAIL (preflight): running DigitalBrain processes hold file locks (P0-9). Stop them first:" -ForegroundColor Red
    $locks | Format-Table Id, ProcessName -AutoSize
    exit 2
}

Write-Host '=== GATE: dotnet build (warnaserror) ==='
if (-not $NoBuild) {
    dotnet build DigitalBrain.slnx -warnaserror --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host 'GATE FAIL: build' -ForegroundColor Red; exit 1 }
}

Write-Host '=== GATE: tests via xunit.v3 in-process runner (no aspire stack!) ==='
# dotnet test's MTP handshake fails against this xunit.v3 exe; the direct runner is the truth.
& src/Tests/DigitalBrain.Tests/bin/Debug/net11.0/DigitalBrain.Tests.exe
if ($LASTEXITCODE -ne 0) { Write-Host 'GATE FAIL: tests' -ForegroundColor Red; exit 1 }

if ($Flutter) {
    foreach ($pkg in 'core', 'kit', 'shell') {
        $path = Join-Path $repo "src/Modules/UI/Flutter/$pkg"
        Write-Host "=== GATE: flutter analyze+test ($pkg) ==="
        Push-Location $path
        flutter analyze
        if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "GATE FAIL: flutter analyze $pkg" -ForegroundColor Red; exit 1 }
        flutter test
        if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "GATE FAIL: flutter test $pkg" -ForegroundColor Red; exit 1 }
        Pop-Location
    }
}

Write-Host 'GATE PASS' -ForegroundColor Green
exit 0
