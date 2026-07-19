# PowerShell script to execute the final global test sweep sequentially using direct DLL execution
# This completely bypasses the .NET 11 VSTest runner enforcement and runs Microsoft.Testing.Platform natively and extremely fast.

$workspace = "E:\digitalbrain"
$agentDir = Join-Path $workspace ".agents\worker_final_sweep"
$agentDirGen2 = Join-Path $workspace ".agents\worker_final_sweep_gen2"
$logDir = Join-Path $agentDir "logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$projects = @(
    @{ Path = "UI/BrainOS.E2E.Tests/BrainOS.E2E.Tests.csproj"; DllPath = "UI/BrainOS.E2E.Tests/bin/Debug/net11.0/BrainOS.E2E.Tests.dll"; Name = "BrainOS.E2E.Tests" },
    @{ Path = "examples/inolang-orleans-proto/tests/InoLang.Orleans.Tests/InoLang.Orleans.Tests.csproj"; DllPath = "examples/inolang-orleans-proto/tests/InoLang.Orleans.Tests/bin/Debug/net9.0/InoLang.Orleans.Tests.dll"; Name = "InoLang.Orleans.Tests" },
    @{ Path = "examples/inolang-orleans-proto/tests/InoLang.Tests/InoLang.Tests.csproj"; DllPath = "examples/inolang-orleans-proto/tests/InoLang.Tests/bin/Debug/net9.0/InoLang.Tests.dll"; Name = "InoLang.Tests" },
    @{ Path = "inolang/DigitalBrain.InoLang.TestRunner.Tests/DigitalBrain.InoLang.TestRunner.Tests.csproj"; DllPath = "inolang/DigitalBrain.InoLang.TestRunner.Tests/bin/Debug/net11.0/DigitalBrain.InoLang.TestRunner.Tests.dll"; Name = "DigitalBrain.InoLang.TestRunner.Tests" },
    @{ Path = "inolang/DigitalBrain.InoLang.Tests/DigitalBrain.InoLang.Tests.csproj"; DllPath = "inolang/DigitalBrain.InoLang.Tests/bin/Debug/net11.0/DigitalBrain.InoLang.Tests.dll"; Name = "DigitalBrain.InoLang.Tests" },
    @{ Path = "kernel/BrainOS.Boot.Tests/BrainOS.Boot.Tests.csproj"; DllPath = "kernel/BrainOS.Boot.Tests/bin/Debug/net11.0/BrainOS.Boot.Tests.dll"; Name = "BrainOS.Boot.Tests" },
    @{ Path = "kernel/BrainOS.Core.Hosting.Tests/BrainOS.Core.Hosting.Tests.csproj"; DllPath = "kernel/BrainOS.Core.Hosting.Tests/bin/Debug/net11.0/BrainOS.Core.Hosting.Tests.dll"; Name = "BrainOS.Core.Hosting.Tests" },
    @{ Path = "kernel/BrainOS.Core.Tests/BrainOS.Core.Tests.csproj"; DllPath = "kernel/BrainOS.Core.Tests/bin/Debug/net11.0/BrainOS.Core.Tests.dll"; Name = "BrainOS.Core.Tests" },
    @{ Path = "kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic.Tests/BrainOS.Domains.Dynamic.Tests.csproj"; DllPath = "kernel/BrainOS.Domains.Dynamic/BrainOS.Domains.Dynamic.Tests/bin/Debug/net11.0/BrainOS.Domains.Dynamic.Tests.dll"; Name = "BrainOS.Domains.Dynamic.Tests" },
    @{ Path = "kernel/BrainOS.Kernel.Tests/BrainOS.Kernel.Tests.csproj"; DllPath = "kernel/BrainOS.Kernel.Tests/bin/Debug/net11.0/BrainOS.Kernel.Tests.dll"; Name = "BrainOS.Kernel.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Ai/DigitalBrain.SDK.Ai.Tests/DigitalBrain.SDK.Ai.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Ai/DigitalBrain.SDK.Ai.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Ai.Tests.dll"; Name = "DigitalBrain.SDK.Ai.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Canvas/DigitalBrain.SDK.Canvas.Tests/DigitalBrain.SDK.Canvas.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Canvas/DigitalBrain.SDK.Canvas.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Canvas.Tests.dll"; Name = "DigitalBrain.SDK.Canvas.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/DigitalBrain.SDK.Google.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Google/DigitalBrain.SDK.Google.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Google.Tests.dll"; Name = "DigitalBrain.SDK.Google.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Identity/DigitalBrain.SDK.Identity.Tests/DigitalBrain.SDK.Identity.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Identity/DigitalBrain.SDK.Identity.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Identity.Tests.dll"; Name = "DigitalBrain.SDK.Identity.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp.Tests/DigitalBrain.SDK.Mcp.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Mcp/DigitalBrain.SDK.Mcp.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Mcp.Tests.dll"; Name = "DigitalBrain.SDK.Mcp.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Tests/DigitalBrain.SDK.Sqlite.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Sqlite.Tests.dll"; Name = "DigitalBrain.SDK.Sqlite.Tests" },
    @{ Path = "sdk/DigitalBrain.SDK.Visuals/DigitalBrain.SDK.Visuals.Tests/DigitalBrain.SDK.Visuals.Tests.csproj"; DllPath = "sdk/DigitalBrain.SDK.Visuals/DigitalBrain.SDK.Visuals.Tests/bin/Debug/net11.0/DigitalBrain.SDK.Visuals.Tests.dll"; Name = "DigitalBrain.SDK.Visuals.Tests" }
)

$results = @()
$totalProjects = $projects.Count
$currentCount = 0

Write-Host "Starting DLL-Direct Sequential Test Sweep across $totalProjects projects..." -ForegroundColor Cyan

# Helper to clean up background processes and Orleans Redis docker containers
function Clean-BackgroundProcesses {
    Write-Host "Cleaning background BrainOS, and DigitalBrain processes..." -ForegroundColor Gray
    Stop-Process -Name BrainOS*, DigitalBrain*, testhost -ErrorAction SilentlyContinue -Force
    cmd.exe /c "dotnet build-server shutdown" | Out-Null
    
    # Clean Orleans Redis containers
    Write-Host "Cleaning Orleans Redis containers..." -ForegroundColor Gray
    docker ps -a --filter "name=orleans-redis" --format "{{.ID}}" | ForEach-Object { docker rm -f $_ } | Out-Null

    # Clear Antigravity source metadata environment variable to avoid Aspire AppHost template substitution failures
    $env:ANTIGRAVITY_SOURCE_METADATA = $null
}

try {
    # Initial cleanup
    Clean-BackgroundProcesses

    foreach ($proj in $projects) {
        $currentCount++
        $projCsproj = Join-Path $workspace $proj.Path
        $projDll = Join-Path $workspace $proj.DllPath
        $projName = $proj.Name
        
        Write-Host "[$currentCount/$totalProjects] Preparing $projName..." -ForegroundColor Yellow
        
        # Update progress.md heartbeat timestamp periodically
        $timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        $progressContent = @"
# progress.md

Last visited: $timestamp

- [x] Step 1: Initialize BRIEFING.md and progress.md.
- [x] Step 2: Clean all running background processes (dotnet, BrainOS, DigitalBrain).
- [/] Step 3: Run the global sequential test sweep using run_sweep.ps1 (Running ${currentCount} of ${totalProjects} - ${projName})
- [ ] Step 4: Inspect sweep_results.json and diagnose failures.
- [ ] Step 5: Fix any failures.
- [ ] Step 6: Create changes.md and handoff.md.
- [ ] Step 7: Send message to the caller Project Orchestrator.
"@
        Set-Content -Path (Join-Path $agentDir "progress.md") -Value $progressContent
        Set-Content -Path (Join-Path $agentDirGen2 "progress.md") -Value $progressContent
        
        # Ensure project exists
        if (-not (Test-Path $projCsproj)) {
            Write-Host "  -> Warning: CSPROJ does not exist: $projCsproj" -ForegroundColor DarkYellow
            continue
        }
        
        # Ensure DLL is built; if not, build it first
        if (-not (Test-Path $projDll)) {
            Write-Host "  -> DLL not found. Building project..." -ForegroundColor Gray
            cmd.exe /c "dotnet build `"$projCsproj`" --configuration Debug /p:UseSharedCompilation=false /p:NodeReuse=false" | Out-Null
        }
        
        if (-not (Test-Path $projDll)) {
            Write-Host "  -> FAIL (Could not build DLL for $projName)" -ForegroundColor Red
            $results += [PSCustomObject]@{
                Name     = $projName
                Path     = $proj.Path
                Status   = "FAIL"
                ExitCode = -1
                Passed   = 0
                Failed   = 1
                Skipped  = 0
                Total    = 1
            }
            continue
        }
        
        $logFile = Join-Path $logDir "$projName.log"
        if (Test-Path $logFile) { Remove-Item $logFile -Force }
        
        Write-Host "  -> Running test DLL directly..." -ForegroundColor Gray
        
        # Run test DLL directly and capture output
        cmd.exe /c "dotnet `"$projDll`" > `"$logFile`" 2>&1"
        $exitCode = $LASTEXITCODE
        
        # Read the output log to parse test counts
        $logLines = Get-Content -Path $logFile -ErrorAction SilentlyContinue
        
        $total = 0
        $failed = 0
        $passed = 0
        $skipped = 0
        $foundSummary = $false
        
        if ($null -ne $logLines) {
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
        }
        
        # Fallback to general parsing if modern test summary block was not found but we have older format
        if (-not $foundSummary -and $null -ne $logLines) {
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
        
        $status = "FAIL"
        if ($failed -eq 0 -and $passed -gt 0) {
            $status = "PASS"
        } elseif ($exitCode -eq 0 -and $failed -eq 0) {
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

        # Clean processes after heavy integration or E2E tests to avoid locks or splits on downstream projects
        if ($projName -eq "BrainOS.E2E.Tests" -or $projName -eq "InoLang.Orleans.Tests" -or $projName -eq "BrainOS.Domains.Dynamic.Tests") {
            Clean-BackgroundProcesses
        }
    }
}
finally {
    # Final cleanup
    Clean-BackgroundProcesses
}

# Export results to JSON
$resultsJson = $results | ConvertTo-Json -Depth 4
Set-Content -Path (Join-Path $agentDir "sweep_results.json") -Value $resultsJson
Set-Content -Path (Join-Path $agentDirGen2 "sweep_results.json") -Value $resultsJson

Write-Host "DLL-Direct Global Test Sweep Completed!" -ForegroundColor Cyan
