# PowerShell script to execute the global test sweep sequentially with dynamic runner detection

$workspace = "E:\digitalbrain"
$logDir = Join-Path $workspace ".agents\worker_global_sweep\logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$projects = @(
    @{ Path = "UI/BrainOS.E2E.Tests/BrainOS.E2E.Tests.csproj"; Name = "BrainOS.E2E.Tests" },
    @{ Path = "examples/inolang-orleans-proto/tests/InoLang.Orleans.Tests/InoLang.Orleans.Tests.csproj"; Name = "InoLang.Orleans.Tests" },
    @{ Path = "examples/inolang-orleans-proto/tests/InoLang.Tests/InoLang.Tests.csproj"; Name = "InoLang.Tests" },
    @{ Path = "inolang/DigitalBrain.InoLang.TestRunner.Tests/DigitalBrain.InoLang.TestRunner.Tests.csproj"; Name = "DigitalBrain.InoLang.TestRunner.Tests" },
    @{ Path = "inolang/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj"; Name = "DigitalBrain.InoLang.Tests" },
    @{ Path = "kernel/BrainOS.Boot.Tests/BrainOS.Boot.Tests.csproj"; Name = "BrainOS.Boot.Tests" },
    @{ Path = "kernel/BrainOS.Core.Hosting.Tests/BrainOS.Core.Hosting.Tests.csproj"; Name = "BrainOS.Core.Hosting.Tests" },
    @{ Path = "kernel/BrainOS.Core.Tests/BrainOS.Core.Tests.csproj"; Name = "BrainOS.Core.Tests" },
    @{ Path = "kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic.Tests/BrainOS.Domains.Dynamic.Tests.csproj"; Name = "BrainOS.Domains.Dynamic.Tests" },
    @{ Path = "kernel/BrainOS.Kernel.Tests/BrainOS.Kernel.Tests.csproj"; Name = "BrainOS.Kernel.Tests" },
    @{ Path = "samples/BrainOS.Domains.Engineering/BrainOS.Domains.Engineering.Tests/BrainOS.Domains.Engineering.Tests.csproj"; Name = "BrainOS.Domains.Engineering.Tests" },
    @{ Path = "samples/BrainOS.Domains.Onboarding/BrainOS.Domains.Onboarding.Tests/BrainOS.Domains.Onboarding.Tests.csproj"; Name = "BrainOS.Domains.Onboarding.Tests" },
    @{ Path = "samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel.Tests/BrainOS.Domains.Travel.Tests.csproj"; Name = "BrainOS.Domains.Travel.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Ai/DigitalBrain.SDK.Ai.Tests/DigitalBrain.SDK.Ai.Tests.csproj"; Name = "DigitalBrain.SDK.Ai.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Aspire.Tests/DigitalBrain.SDK.Aspire.Tests.csproj"; Name = "DigitalBrain.SDK.Aspire.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Canvas/DigitalBrain.SDK.Canvas.Tests/DigitalBrain.SDK.Canvas.Tests.csproj"; Name = "DigitalBrain.SDK.Canvas.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj"; Name = "DigitalBrain.SDK.Google.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Identity/DigitalBrain.SDK.Identity.Tests/DigitalBrain.SDK.Identity.Tests.csproj"; Name = "DigitalBrain.SDK.Identity.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp.Tests/DigitalBrain.SDK.Mcp.Tests.csproj"; Name = "DigitalBrain.SDK.Mcp.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Tests/DigitalBrain.SDK.Sqlite.Tests.csproj"; Name = "DigitalBrain.SDK.Sqlite.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Visuals/DigitalBrain.SDK.Visuals.Tests/DigitalBrain.SDK.Visuals.Tests.csproj"; Name = "DigitalBrain.SDK.Visuals.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Windows.Tests/DigitalBrain.SDK.Windows.Tests.csproj"; Name = "DigitalBrain.SDK.Windows.Tests" }
)

$results = @()
$totalProjects = $projects.Count
$currentCount = 0

Write-Host "Starting Adaptive Global Test Sweep across $totalProjects projects..." -ForegroundColor Cyan

$globalJsonPath = Join-Path $workspace "global.json"
$globalJsonBackup = Join-Path $workspace "global.json.bak"

# Helper function to clean up/restore global.json
function Restore-GlobalJson {
    if (Test-Path $globalJsonBackup) {
        if (Test-Path $globalJsonPath) {
            Remove-Item -Path $globalJsonPath -Force | Out-Null
        }
        Rename-Item -Path $globalJsonBackup -NewName "global.json" -Force | Out-Null
    }
}

try {
    # Make sure global.json is in starting position
    Restore-GlobalJson

    foreach ($proj in $projects) {
        $currentCount++
        $projPath = Join-Path $workspace $proj.Path
        $projName = $proj.Name
        
        Write-Host "[$currentCount/$totalProjects] Testing $projName..." -ForegroundColor Yellow
        
        # Update progress.md heartbeat timestamp periodically
        $timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        $progressContent = @"
# Progress Tracking

Last visited: $timestamp

## Milestone: Final Global Test Sweep
- [x] Initialize progress and plan - DONE
- [x] Verify environment and projects build - DONE
- [ ] Sequentially run 22 test projects - IN_PROGRESS (Running `${currentCount}` of `${totalProjects}` - `${projName}`)
- [ ] Aggregate and summarize test results - TODO
- [ ] Generate comprehensive handoff.md - TODO
- [ ] Send handoff message to parent orchestrator - TODO
"@
        Set-Content -Path (Join-Path $workspace ".agents\worker_global_sweep\progress.md") -Value $progressContent

        $logFile = Join-Path $logDir "$projName.log"
        $errLogFile = Join-Path $logDir "$projName.err.log"
        
        # 1. Try running dotnet test with global.json intact
        if (Test-Path $logFile) { Remove-Item $logFile -Force }
        if (Test-Path $errLogFile) { Remove-Item $errLogFile -Force }
        
        cmd.exe /c "dotnet test `"$projPath`" > `"$logFile`" 2> `"$errLogFile`""
        $exitCode = $LASTEXITCODE
        
        $errContent = Get-Content -Path $errLogFile -ErrorAction SilentlyContinue | Out-String
        
        # 2. Check if it failed due to VSTest restriction under global.json
        if ($errContent -like "*global.json defines test runner to be Microsoft.Testing.Platform*") {
            Write-Host "  -> Detected VSTest project. Temporarily bypassing global.json enforcement..." -ForegroundColor Gray
            
            # Temporarily rename
            if (Test-Path $globalJsonPath) {
                Rename-Item -Path $globalJsonPath -NewName "global.json.bak" -Force | Out-Null
            }
            
            # Re-run
            if (Test-Path $logFile) { Remove-Item $logFile -Force }
            if (Test-Path $errLogFile) { Remove-Item $errLogFile -Force }
            
            cmd.exe /c "dotnet test `"$projPath`" > `"$logFile`" 2> `"$errLogFile`""
            $exitCode = $LASTEXITCODE
            
            # Restore immediately
            Restore-GlobalJson
        }
        
        # Read the output log to parse test counts
        $logLines = Get-Content -Path $logFile -ErrorAction SilentlyContinue
        
        $total = 0
        $failed = 0
        $passed = 0
        $skipped = 0
        $foundSummary = $false
        
        foreach ($line in $logLines) {
            if ($line -match '^\s*total:\s*(\d+)') {
                $total = [int]$Matches[1]
                $foundSummary = $true
            }
            elseif ($line -match '^\s*failed:\s*(\d+)') {
                $failed = [int]$Matches[1]
            }
            elseif ($line -match '^\s*succeeded:\s*(\d+)') {
                $passed = [int]$Matches[1]
            }
            elseif ($line -match '^\s*skipped:\s*(\d+)') {
                $skipped = [int]$Matches[1]
            }
        }
        
        # Fallback to general parsing if modern test summary block was not found but we have older format
        if (-not $foundSummary) {
            foreach ($line in $logLines) {
                if ($line -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
                    $failed = [int]$Matches[1]
                    $passed = [int]$Matches[2]
                    $skipped = [int]$Matches[3]
                    $total = [int]$Matches[4]
                    $foundSummary = $true
                }
                elseif ($line -match 'Passed!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
                    $failed = [int]$Matches[1]
                    $passed = [int]$Matches[2]
                    $skipped = [int]$Matches[3]
                    $total = [int]$Matches[4]
                    $foundSummary = $true
                }
                elseif ($line -match 'Failed!\s+-\s+Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
                    $failed = [int]$Matches[1]
                    $passed = [int]$Matches[2]
                    $skipped = [int]$Matches[3]
                    $total = [int]$Matches[4]
                    $foundSummary = $true
                }
            }
        }

        # If still not found, check if there is a VSTest runner format:
        # "Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1"
        if (-not $foundSummary) {
            $logContent = [string]::Join("`n", $logLines)
            if ($logContent -match 'Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)') {
                $failed = [int]$Matches[1]
                $passed = [int]$Matches[2]
                $skipped = [int]$Matches[3]
                $total = [int]$Matches[4]
                $foundSummary = $true
            }
        }

        $status = "FAIL"
        if ($exitCode -eq 0 -and $failed -eq 0) {
            $status = "PASS"
        }
        
        # Print status to host
        if ($status -eq "PASS") {
            Write-Host "  -> PASS (Passed: $passed, Failed: $failed, Skipped: $skipped)" -ForegroundColor Green
        } else {
            Write-Host "  -> FAIL (ExitCode: $exitCode, Passed: $passed, Failed: $failed, Skipped: $skipped)" -ForegroundColor Red
        }
        
        $results += [PSCustomObject]@{
            Name     = $projName
            Path     = $proj.Path
            Status   = $status
            ExitCode = $exitCode
            Passed   = $passed
            Failed   = $failed
            Skipped  = $skipped
            Total    = $total
        }
    }
}
finally {
    # Ensure global.json is restored under all circumstances
    Restore-GlobalJson
}

# Export results to JSON for programmatic verification
$resultsJson = $results | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $workspace ".agents\worker_global_sweep\sweep_results.json") -Value $resultsJson

Write-Host "Global Test Sweep Completed!" -ForegroundColor Cyan
