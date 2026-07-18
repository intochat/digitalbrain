#!/usr/bin/env pwsh
<#
run-ci.ps1 — one root command for build + all tests (the CI gate).
High severity: full suite must be green before commit. Gate MUST be able to fail.
Usage: ./run-ci.ps1
After: aspire ps / logs for resources if running via `aspire run`.
#>

$ErrorActionPreference = 'Stop'

Write-Host '=== DigitalBrain CI: build + test (high severity) ===' -ForegroundColor Cyan

Push-Location $PSScriptRoot

try {
    Write-Host 'dotnet build ...' -ForegroundColor Yellow
    dotnet build -v minimal
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }

    # Automate (Elon pass): git diff --check to catch trailing ws / crlf-in-diff etc on every high-sev run.
    Write-Host 'git diff --check (whitespace gate)' -ForegroundColor Yellow
    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed (whitespace or line-ending issues in diff)' }

    # Pack verification (applied from PR#1 review - accelerate Step 4 + automate Step 5 after deletes/simplifies).
    # Exercises IsPackable + PackageId + Generate=false props on the 4 packables (Core/Contracts, Sdk, Ino, Connectors)
    # per polyrepo DAG (vision §4 + decomp plan). Use -c Debug to match the preceding 'dotnet build' (which defaults to Debug).
    # Use full .csproj paths for reliability. Dry to local dir then cleaned; keeps gate fast while proving
    # "ready for publish/split" on every high-sev run. No behavior change.
    $packTs = Get-Date -Format 'yyyyMMdd-HHmmss'
    $packDir = "TestResults/pack-verify-$packTs"
    if (Test-Path $packDir) { Remove-Item -Recurse -Force $packDir -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Path $packDir | Out-Null
    $packables = @(
        "src/DigitalBrain.Os/DigitalBrain.Os.csproj",
        "src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj",
        "src/DigitalBrain.Ino/DigitalBrain.Ino.csproj",
        "src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj"
    )
    foreach ($p in $packables) {
        dotnet pack $p -v quiet --no-build -c Debug -o $packDir --nologo
        if ($LASTEXITCODE -ne 0) { throw "pack verification failed for $p (review props)" }
    }
    Remove-Item -Recurse -Force $packDir -ErrorAction SilentlyContinue

    # SKIP_FLUTTER_RESOURCE prevents flutter-client AddExecutable from triggering the
    # permission_handler_windows MSVC coroutine deprecation build during Aspire model/test host.
    # _SILENCE is also exported for any direct toolchain that reaches cl.exe.
    $env:SKIP_FLUTTER_RESOURCE = "1"
    $env:_SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS = "1"

    $ts = Get-Date -Format 'yyyyMMdd-HHmmss'
    $resultsDir = "TestResults/ci-$ts"
    if (Test-Path $resultsDir) { Remove-Item -Recurse -Force $resultsDir -ErrorAction SilentlyContinue }

    # Pre-clean pa-files under test bin (pack writes relative pa-files/packages from test process cwd;
    # prevents lock contention on .brain files across agent-invoked runs in same env).
    $pa = "src/DigitalBrain.Os.Tests/bin/Debug/net11.0/pa-files"
    if (Test-Path $pa) { Remove-Item -Recurse -Force $pa -ErrorAction SilentlyContinue }

    $testExe = "src/DigitalBrain.Os.Tests/bin/Debug/net11.0/DigitalBrain.Os.Tests.exe"
    if (-not (Test-Path $testExe)) { throw "test exe not found after build: $testExe" }

    $infraFlakePattern = 'being used by another process|MSB3491|CS2012|named pipe|The process cannot access the file'

    function Invoke-Tests {
        $output = & $testExe --no-banner --minimum-expected-tests 40 2>&1 | Out-String
        $exit = $LASTEXITCODE
        return [pscustomobject]@{ Output = $output; Exit = $exit }
    }

    function Measure-FailedCount ([string]$output) {
        # xUnit v3/MTP summary: "  failed: N"
        if ($output -match '[Ff]ailed:\s*(\d+)') { return [int]$Matches[1] }
        return -1  # missing summary — treat as failure
    }

    function Test-SummaryPresent ([string]$output) {
        return $output -match 'Test run summary:'
    }

    Write-Host 'Running FULL test assembly ...' -ForegroundColor Yellow
    $run = Invoke-Tests
    Write-Host $run.Output

    $failed = Measure-FailedCount $run.Output
    $summaryFound = Test-SummaryPresent $run.Output
    $failed = if (-not $summaryFound) { -1 } else { $failed }

    $isFailure = ($run.Exit -ne 0) -or ($failed -ne 0)

    # Retry ONCE only on detected infrastructure flakiness (file lock / named pipe / build-output lock).
    # Never retry a real test-assertion failure.
    if ($isFailure -and ($run.Output -match $infraFlakePattern)) {
        Write-Host 'Infrastructure flake detected — retrying once ...' -ForegroundColor Yellow
        $pa = "src/DigitalBrain.Os.Tests/bin/Debug/net11.0/pa-files"
        if (Test-Path $pa) { Remove-Item -Recurse -Force $pa -ErrorAction SilentlyContinue }

        $run = Invoke-Tests
        Write-Host $run.Output

        $failed = Measure-FailedCount $run.Output
        $summaryFound = Test-SummaryPresent $run.Output
        $failed = if (-not $summaryFound) { -1 } else { $failed }

        $isFailure = ($run.Exit -ne 0) -or ($failed -ne 0)
    }

    if ($isFailure) {
        throw "tests failed (failed=$failed, exit=$($run.Exit))"
    }

    Write-Host '=== GREEN ===' -ForegroundColor Green
    Write-Host 'Optional: aspire run (from src/DigitalBrain.AppHost or root with aspire.config.json)'
    Write-Host 'aspire ps / logs / describe for inspection.'
}
finally {
    Pop-Location
}
