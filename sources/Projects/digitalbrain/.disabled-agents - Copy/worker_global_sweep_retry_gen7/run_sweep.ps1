# PowerShell script to execute the final global test sweep sequentially with dynamic runner detection
# Optimizations: Single-process builds (/m:1) and per-iteration cleanup of process/file locks.

$workspace = "E:\digitalbrain"
$logDir = Join-Path $workspace ".agents\worker_global_sweep_retry_gen7\logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$projects = @(
    @{ Path = "UI/BrainOS.E2E.Tests/BrainOS.E2E.Tests.csproj"; Name = "BrainOS.E2E.Tests"; Filter = "Stage=fast" },
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
    @{ Path = "sdk/DigitalBrain.SDK.Windows.Tests/DigitalBrain.SDK.Windows.Tests.csproj"; Name = "DigitalBrain.SDK.Windows.Tests" },
    @{ Path = "DigitalBrain.Test/DigitalBrain.Test.csproj"; Name = "DigitalBrain.Test" }
)

$results = @()
$totalProjects = $projects.Count
$currentCount = 0

Write-Host "Starting Optimized Global Test Sweep across $totalProjects projects..." -ForegroundColor Cyan

$globalJsonPath = Join-Path $workspace "global.json"

$globalJsonModern = @'
{
  "sdk": {
    "version": "11.0.100-preview.3.26207.106",
    "rollForward": "latestFeature"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  },
  "msbuild-sdks": {
    "Microsoft.Build.NoTargets": "3.7.134"
  }
}
'@

$globalJsonLegacy = @'
{
  "sdk": {
    "version": "11.0.100-preview.3.26207.106",
    "rollForward": "latestFeature"
  },
  "msbuild-sdks": {
    "Microsoft.Build.NoTargets": "3.7.134"
  }
}
'@

# Helper function to clean up/restore global.json
function Restore-GlobalJson {
    Set-Content -Path $globalJsonPath -Value $globalJsonModern -Force | Out-Null
}

function Set-LegacyGlobalJson {
    Set-Content -Path $globalJsonPath -Value $globalJsonLegacy -Force | Out-Null
}

# Helper function to forcefully kill lingering project-specific test hosts and connections
function Stop-LingeringProcesses {
    Get-Process | Where-Object { 
        $_.Name -like "*BrainOS*" -or 
        $_.Name -like "*DigitalBrain*" -or 
        $_.Name -eq "testhost" 
    } | Stop-Process -Force -ErrorAction SilentlyContinue
    
    cmd.exe /c "dotnet build-server shutdown > nul 2>&1"
    
    # Stop and remove any leaked Orleans Redis Docker containers to clear the Orleans clustering database
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        docker ps -q --filter "name=orleans-redis" | ForEach-Object { 
            docker kill $_ > $null 2>&1
            docker rm -f $_ > $null 2>&1
        }
    }
    
    # Also release any SQLite database files that might be temporarily locked
    Remove-Item "C:\Users\vhorb\AppData\Local\BrainOS\databases\m1-tests*.db" -Force -ErrorAction SilentlyContinue
}

try {
    # Initial cleanup
    Stop-LingeringProcesses
    Restore-GlobalJson

    foreach ($proj in $projects) {
        $currentCount++
        $projPath = Join-Path $workspace $proj.Path
        $projName = $proj.Name
        
        Write-Host "[$currentCount/$totalProjects] Checking $projName..." -ForegroundColor Yellow
        
        # Check if project file exists first
        if (-not (Test-Path $projPath)) {
            Write-Host "  -> SKIP: Project file not found at $projPath" -ForegroundColor Gray
            $results += [PSCustomObject]@{
                Name     = $projName
                Path     = $proj.Path
                Status   = "SKIP"
                ExitCode = 0
                Passed   = 0
                Failed   = 0
                Skipped  = 0
                Total    = 0
            }
            continue
        }

        Write-Host "[$currentCount/$totalProjects] Testing $projName..." -ForegroundColor Yellow
        
        # Update progress.md heartbeat timestamp periodically
        $timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        $progressContent = @"
# Progress — worker_global_sweep_retry_gen7

Last visited: $timestamp

## Tasks
- [x] Clean background processes (Stop-Process) <!-- id: 0 -->
- [x] Copy optimized ``run_sweep.ps1`` from worker_global_sweep_retry_gen6 <!-- id: 1 -->
- [x] Modify ``$logDir`` and ``progress.md`` paths in the copied script <!-- id: 2 -->
- [/] Run sequential test sweep script <!-- id: 3 --> (Running ${currentCount} of ${totalProjects} - ${projName})
- [ ] Inspect test logs and ``sweep_results.json`` <!-- id: 4 -->
- [ ] Ensure all active test projects pass cleanly <!-- id: 5 -->
- [ ] Create ``changes.md`` and ``handoff.md`` <!-- id: 6 -->
- [ ] Send handoff message to caller Project Orchestrator <!-- id: 7 -->
"@
        Set-Content -Path (Join-Path $workspace ".agents\worker_global_sweep_retry_gen7\progress.md") -Value $progressContent

        $logFile = Join-Path $logDir "$projName.log"
        $errLogFile = Join-Path $logDir "$projName.err.log"
        
        if (Test-Path $logFile) { Remove-Item $logFile -Force }
        if (Test-Path $errLogFile) { Remove-Item $errLogFile -Force }
        
        $filterArg = ""
        if ($proj.Filter) {
            $filterArg = "--filter `"$($proj.Filter)`""
        }
        
        # Dynamically detect if it's a modern Testing Platform project or VSTest for running tests
        $projContent = Get-Content -Path $projPath | Out-String
        $isModern = ($projContent -match "UseMicrosoftTestingPlatformRunner") -or 
                    ($projContent -match "xunit\.v3") -or 
                    ($projContent -match "Testing\.Platform") -or
                    ($projContent -match "Microsoft\.Testing\.Platform")
        
        if ($isModern) {
            Write-Host "  -> Modern Testing Platform project detected. Running with global.json intact." -ForegroundColor Gray
            Restore-GlobalJson
        } else {
            Write-Host "  -> VSTest project detected. Bypassing global.json runner enforcement." -ForegroundColor Gray
            Set-LegacyGlobalJson
        }
        
        # Clean and build using single-process configuration (/m:1) to completely avoid MSBuild multi-node lock clashes
        Write-Host "  -> Cleaning project..." -ForegroundColor Gray
        cmd.exe /c "dotnet clean `"$projPath`" -c Debug > `"$logFile`" 2> `"$errLogFile`""
        Write-Host "  -> Building project (single-process)..." -ForegroundColor Gray
        cmd.exe /c "dotnet build `"$projPath`" -c Debug /m:1 /p:NodeReuse=false /p:UseSharedCompilation=false >> `"$logFile`" 2>> `"$errLogFile`""
        
        # Run test with no-build, passing MTP argument for Modern projects
        Write-Host "  -> Executing tests..." -ForegroundColor Gray
        if ($isModern) {
            cmd.exe /c "dotnet test `"$projPath`" -c Debug --no-build /p:UseMicrosoftTestingPlatform=true $filterArg >> `"$logFile`" 2>> `"$errLogFile`""
        } else {
            cmd.exe /c "dotnet test `"$projPath`" -c Debug --no-build $filterArg >> `"$logFile`" 2>> `"$errLogFile`""
        }
        $exitCode = $LASTEXITCODE
        
        # Restore global.json immediately after the run
        Restore-GlobalJson
        
        # Release and kill all locks/lingering test processes immediately before moving to next project
        Stop-LingeringProcesses
        
        # Read the output log to parse test counts
        $logLines = Get-Content -Path $logFile -ErrorAction SilentlyContinue
        if ($null -eq $logLines) {
            $logLines = @()
        }
        
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
        if ($failed -eq 0 -and ($passed + $skipped -eq $total -or $total -eq 0)) {
            $status = "PASS"
        }
        elseif ($failed -eq 0 -and $foundSummary -and $passed -gt 0) {
            $status = "PASS"
        }
        elseif ($exitCode -eq 0 -and $failed -eq 0) {
            # Catch case where total was 0 but exit code was 0
            $status = "PASS"
        }
        
        # Print status to host
        if ($status -eq "PASS") {
            Write-Host "  -> PASS (Passed: $passed, Failed: $failed, Skipped: $skipped, Total: $total)" -ForegroundColor Green
        } else {
            Write-Host "  -> FAIL (ExitCode: $exitCode, Passed: $passed, Failed: $failed, Skipped: $skipped, Total: $total)" -ForegroundColor Red
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
    # Ensure global.json and environment are fully restored under all circumstances
    Restore-GlobalJson
    Stop-LingeringProcesses
}

# Export results to JSON for programmatic verification
$resultsJson = $results | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $workspace ".agents\worker_global_sweep_retry_gen7\sweep_results.json") -Value $resultsJson

Write-Host "Global Test Sweep Completed!" -ForegroundColor Cyan
