param(
    [string]$Owner = 'quickstart-user',
    [string]$AspireCommand = 'aspire',
    [string]$DotnetCommand = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$quickstartRoot = $PSScriptRoot
$appHost = Join-Path $quickstartRoot 'DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj'
$console = Join-Path $quickstartRoot 'DigitalBrain.Quickstart.Console/DigitalBrain.Quickstart.Console.csproj'
$appHostPath = [System.IO.Path]::GetFullPath($appHost)

function Test-QuickstartRunning {
    $output = & $AspireCommand ps --format Json --non-interactive --nologo
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "aspire ps failed with exit code $exitCode."
    }
    $running = @($output | ConvertFrom-Json)
    return [bool]($running | Where-Object {
            $null -ne $_ -and
            -not [string]::IsNullOrWhiteSpace($_.appHostPath) -and
            [System.IO.Path]::GetFullPath($_.appHostPath) -eq $appHostPath
        })
}

if (Test-QuickstartRunning) {
    throw 'The DigitalBrain quickstart AppHost is already running.'
}

$previousOwner = $env:Parameters__digitalbrain_owner
$previousEnvironment = @{}
$operationError = $null
$cleanupErrors = [System.Collections.Generic.List[string]]::new()
$startAttempted = $false
try {
    $env:Parameters__digitalbrain_owner = $Owner
    foreach ($name in @('DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')) {
        $previousEnvironment[$name] =
            [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable(
            $name,
            'Development',
            'Process')
    }
    $startAttempted = $true
    & $AspireCommand start --apphost $appHost --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "aspire start failed with exit code $LASTEXITCODE."
    }

    & $AspireCommand wait kernel --apphost $appHost --status up --timeout 120 --non-interactive --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "aspire wait failed with exit code $LASTEXITCODE."
    }

    & $AspireCommand resource console start --apphost $appHost --non-interactive --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "aspire resource console start failed with exit code $LASTEXITCODE."
    }
    & $AspireCommand wait console --apphost $appHost --status up --timeout 120 --non-interactive --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "aspire wait console failed with exit code $LASTEXITCODE."
    }

    $description = & $AspireCommand describe console --apphost $appHost --format Json --include-hidden --non-interactive --nologo |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        throw "aspire describe failed with exit code $LASTEXITCODE."
    }
    $resource = @($description.resources)[0]
    if ($null -eq $resource) {
        throw 'The console resource was not found.'
    }
    & $AspireCommand resource console stop --apphost $appHost --non-interactive --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "aspire resource console stop failed with exit code $LASTEXITCODE."
    }

    foreach ($property in $resource.environment.PSObject.Properties) {
        if (-not $previousEnvironment.ContainsKey($property.Name)) {
            $previousEnvironment[$property.Name] =
                [Environment]::GetEnvironmentVariable($property.Name, 'Process')
        }
        [Environment]::SetEnvironmentVariable(
            $property.Name,
            [string]$property.Value,
            'Process')
    }
    & $DotnetCommand run --project $console --no-build --no-launch-profile
    if ($LASTEXITCODE -ne 0) {
        throw "The DigitalBrain console failed with exit code $LASTEXITCODE."
    }
}
catch {
    $operationError = $_
}
finally {
    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            'Process')
    }
    $env:Parameters__digitalbrain_owner = $previousOwner
    $shouldStop = $false
    try {
        $shouldStop = Test-QuickstartRunning
    }
    catch {
        $cleanupErrors.Add($_.Exception.Message)
        $shouldStop = $startAttempted
    }
    if ($shouldStop) {
        try {
            & $AspireCommand stop --apphost $appHost --non-interactive --nologo
            if ($LASTEXITCODE -ne 0) {
                throw "aspire stop failed with exit code $LASTEXITCODE."
            }
        }
        catch {
            $cleanupErrors.Add($_.Exception.Message)
        }
    }
}

if ($null -ne $operationError) {
    if ($cleanupErrors.Count -gt 0) {
        throw (
            "$($operationError.Exception.Message) " +
            "Cleanup also failed: $($cleanupErrors -join ' ')")
    }
    throw $operationError
}
if ($cleanupErrors.Count -gt 0) {
    throw ($cleanupErrors -join ' ')
}
