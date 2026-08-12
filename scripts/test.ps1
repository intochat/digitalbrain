# Module-owned test gate (no central suite).
# xUnit v3 runs as executables (Microsoft.Testing.Platform handshake with
# `dotnet test` is unreliable on this SDK preview); build then invoke each .exe.
param(
    [switch]$Flutter
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "dotnet build DigitalBrain.slnx -warnaserror --nologo"
dotnet build DigitalBrain.slnx -warnaserror --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$failed = 0
$projects = Get-ChildItem -Path $root\src -Recurse -Filter '*.Tests.csproj'
foreach ($proj in $projects) {
    $name = $proj.BaseName
    $exe = Join-Path $proj.DirectoryName "bin\Debug\net11.0\$name.exe"
    if (-not (Test-Path $exe)) {
        Write-Error "Missing test host: $exe"
        $failed++
        continue
    }

    Write-Host "=== $name ==="
    & $exe
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED $name (exit $LASTEXITCODE)"
        $failed++
    }
}

if ($Flutter) {
    foreach ($pkg in @('core', 'kit', 'shell')) {
        $dir = Join-Path $root "src\Modules\UI\Flutter\$pkg"
        if (-not (Test-Path (Join-Path $dir 'test'))) {
            continue
        }
        Write-Host "=== flutter test ($pkg) ==="
        Push-Location $dir
        try {
            flutter test
            if ($LASTEXITCODE -ne 0) {
                $failed++
            }
        }
        finally {
            Pop-Location
        }
    }
}

if ($failed -gt 0) {
    Write-Host "Test gate failed ($failed project(s))."
    exit 1
}

Write-Host "All module-owned tests green."
