<#
.SYNOPSIS
Runs the fast cleanup verification loop for NeuroOS / DigitalBrain.

.DESCRIPTION
This is the small local loop used by the cleanup phases. It stops a running Aspire
AppHost before build-backed dotnet tests to avoid Windows file locks, then restarts
and waits the kernel/flutter resources only if Aspire was already running or
-StartAspire is supplied.

.PARAMETER IncludeFlutter
Also run the targeted Flutter shell/action-dispatch analyze and tests.

.PARAMETER NoBuild
Pass --no-build to dotnet test and leave Aspire running.

.PARAMETER SkipAspire
Do not stop, restart, wait, or doctor Aspire. Use only when you know no AppHost
is holding build outputs.

.PARAMETER StartAspire
Start and wait Aspire after verification even if it was not running at entry.

.PARAMETER KeepAspireStopped
If Aspire was stopped for verification, do not restart it at the end.
#>
[CmdletBinding()]
param(
    [switch]$IncludeFlutter,
    [switch]$NoBuild,
    [switch]$SkipAspire,
    [switch]$StartAspire,
    [switch]$KeepAspireStopped
)

# Do not enable global StrictMode or ErrorActionPreference=Stop here. Flutter's
# Windows launcher can stall before dart.exe starts under those PowerShell
# policies. This script checks external-process exit codes explicitly instead.

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$AppHost = 'DigitalBrain.AppHost\DigitalBrain.AppHost.csproj'
$KernelFilter = @(
    'FullyQualifiedName~HomeFeedBusTests',
    'FullyQualifiedName~HomeFeedCrossSiloTests',
    'FullyQualifiedName~GatewayServiceTests',
    'FullyQualifiedName~UserSessionNeuronTests',
    'FullyQualifiedName~UserSessionNeuronClientIdTests',
    'FullyQualifiedName~ExperienceStepDispatchTests',
    'FullyQualifiedName~InoNeuronChatSurfaceTests',
    'FullyQualifiedName~DigitalBrainModelRegistryTests',
    'FullyQualifiedName~ChatClientRegistrationTests'
) -join '|'

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Body
    )

    Write-Host ''
    Write-Host "==> $Name"
    & $Body
    Write-Host "ok: $Name"
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$File,
        [string[]]$Arguments = @()
    )

    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function ConvertFrom-MixedJson {
    param([string[]]$Lines)

    $normalized = @($Lines | Where-Object { $null -ne $_ })
    if ($normalized.Count -eq 0) {
        return $null
    }

    $startLine = $null
    for ($i = 0; $i -lt $normalized.Count; $i++) {
        $trimmed = $normalized[$i].TrimStart()
        if ($trimmed.StartsWith('{') -or $trimmed.StartsWith('[')) {
            $startLine = $i
            break
        }
    }

    if ($null -eq $startLine) {
        $text = ($normalized | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            return $null
        }

        throw "No JSON payload found in command output."
    }

    $text = ($normalized[$startLine..($normalized.Count - 1)] | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    return $text | ConvertFrom-Json
}

function Get-RunningAspireApps {
    $raw = & aspire ps --format Json --non-interactive 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @()
    }

    $parsed = ConvertFrom-MixedJson $raw
    if ($null -eq $parsed) {
        return @()
    }

    return @($parsed | Where-Object { $_.status -eq 'running' })
}

function Start-AspireAndWait {
    Invoke-Step 'aspire start' {
        $raw = & aspire start --no-build --apphost $AppHost --format Json --non-interactive
        if ($LASTEXITCODE -ne 0) {
            throw "aspire start failed with exit code $LASTEXITCODE"
        }

        $started = ConvertFrom-MixedJson $raw
        Write-Host "Started Aspire AppHost PID $($started.appHostPid)."
    }

    $describeRaw = & aspire describe --format Json --non-interactive
    if ($LASTEXITCODE -ne 0) {
        throw "aspire describe failed with exit code $LASTEXITCODE"
    }

    $describe = ConvertFrom-MixedJson $describeRaw
    $resources = @(
        $describe.resources |
            Where-Object { $_.displayName -eq 'kernel' -or $_.displayName -eq 'flutter-ui' } |
            Select-Object -ExpandProperty name
    )

    foreach ($resource in $resources) {
        Invoke-Step "aspire wait $resource" {
            Invoke-External 'aspire' @('wait', $resource, '--non-interactive')
        }
    }
}

Push-Location $RepoRoot
$shouldRestartAspire = $false
try {
    if ($SkipAspire -and -not $NoBuild) {
        Write-Warning 'SkipAspire with build-backed tests can hit Aspire-held Windows file locks.'
    }

    $runningApps = @(if ($SkipAspire) { @() } else { Get-RunningAspireApps })
    $shouldRestartAspire = (-not $SkipAspire) -and $StartAspire -and ($runningApps.Count -eq 0)

    if ((-not $SkipAspire) -and $runningApps.Count -gt 0 -and -not $NoBuild) {
        Invoke-Step 'aspire stop' {
            Invoke-External 'aspire' @('stop', '--non-interactive')
        }
        $shouldRestartAspire = $true
    }

    if ($IncludeFlutter) {
        Invoke-External 'cmd.exe' @('/d', '/c', (Join-Path $RepoRoot 'scripts\verify-flutter.cmd'))
    }

    $kernelTestArgs = @('test', 'DigitalBrain.Tests\DigitalBrain.Tests.csproj', '-m:1')
    if ($NoBuild) {
        $kernelTestArgs += '--no-build'
    }
    $kernelTestArgs += @('--filter', $KernelFilter)

    Invoke-Step 'dotnet test cleanup kernel slice' {
        Invoke-External 'dotnet' $kernelTestArgs
    }

    $salesforceTestArgs = @('test', 'DigitalBrain.Salesforce.Tests\DigitalBrain.Salesforce.Tests.csproj', '-m:1')
    if ($NoBuild) {
        $salesforceTestArgs += '--no-build'
    }

    Invoke-Step 'dotnet test Salesforce slice' {
        Invoke-External 'dotnet' $salesforceTestArgs
    }

    if (-not $SkipAspire) {
        Invoke-Step 'aspire doctor' {
            Invoke-External 'aspire' @('doctor', '--non-interactive')
        }
    }
}
finally {
    if ($shouldRestartAspire -and -not $KeepAspireStopped) {
        Start-AspireAndWait
    }
    elseif ($shouldRestartAspire -and $KeepAspireStopped) {
        Write-Host 'Aspire restart skipped because -KeepAspireStopped was supplied.'
    }

    Pop-Location
}
