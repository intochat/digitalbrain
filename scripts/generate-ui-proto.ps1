param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-CheckedTool {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    $output = @(& $Command @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed."
    }
    return @($output | ForEach-Object { $_.ToString() })
}

function Write-NormalizedDart {
    param(
        [string]$Path,
        [bool]$StripComments
    )

    $text = [IO.File]::ReadAllText($Path)
    $lines = [Text.RegularExpressions.Regex]::Split($text, "`r`n|`n|`r")
    if ($StripComments) {
        $lines = @($lines | Where-Object { $_ -notmatch '^\s*//' })
    }
    $normalized = $lines -join "`n"
    if (-not $normalized.EndsWith("`n", [StringComparison]::Ordinal)) {
        $normalized += "`n"
    }
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

function Test-EqualBytes {
    param(
        [byte[]]$Left,
        [byte[]]$Right
    )

    if ($Left.Length -ne $Right.Length) {
        return $false
    }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) {
            return $false
        }
    }
    return $true
}

foreach ($tool in @('protoc', 'dart', 'protoc-gen-dart')) {
    if ($null -eq (Get-Command -Name $tool -CommandType Application -ErrorAction SilentlyContinue)) {
        throw "$tool is required."
    }
}

$protocVersion = ((Invoke-CheckedTool -Command 'protoc' -Arguments @('--version')) -join "`n").Trim()
if ($protocVersion -cne 'libprotoc 35.0') {
    throw "libprotoc 35.0 is required."
}

$dartVersion = ((Invoke-CheckedTool -Command 'dart' -Arguments @('--version')) -join "`n").Trim()
if ($dartVersion -notmatch '^Dart SDK version: 3\.12\.2(?:\s|$)') {
    throw "Dart SDK 3.12.2 is required."
}

$globalPackages = @(Invoke-CheckedTool -Command 'dart' -Arguments @('pub', 'global', 'list'))
$pluginPackages = @($globalPackages | Where-Object { $_ -match '^protoc_plugin\s' })
if ($pluginPackages.Count -ne 1 -or $pluginPackages[0] -cne 'protoc_plugin 25.0.0') {
    throw "protoc_plugin 25.0.0 must be the active global package."
}

$pubCache = if ([string]::IsNullOrWhiteSpace($env:PUB_CACHE)) {
    Join-Path $env:LOCALAPPDATA 'Pub\Cache'
} else {
    $env:PUB_CACHE
}
$expectedPluginDirectory = [IO.Path]::GetFullPath((Join-Path $pubCache 'bin'))
$pluginCommand = Get-Command -Name 'protoc-gen-dart' -CommandType Application
$pluginDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $pluginCommand.Source))
if (-not [string]::Equals($pluginDirectory, $expectedPluginDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "protoc-gen-dart must resolve from the active Dart global package cache."
}

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$protoDirectory = Join-Path $root 'src\DigitalBrain.Mcp\Protos'
$protoPath = Join-Path $protoDirectory 'ui.proto'
$destinationDirectory = Join-Path $root 'app\lib\grpc'
$rawNames = @('ui.pb.dart', 'ui.pbenum.dart', 'ui.pbgrpc.dart', 'ui.pbjson.dart')
$trackedNames = @('ui.pb.dart', 'ui.pbenum.dart', 'ui.pbgrpc.dart')
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("digitalbrain-ui-proto-{0}" -f [guid]::NewGuid().ToString('N'))))
$tempPrefix = $tempBase.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not (Split-Path -Leaf $tempRoot).StartsWith('digitalbrain-ui-proto-', [StringComparison]::Ordinal)) {
    throw "The temporary generation path is invalid."
}

[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $null = Invoke-CheckedTool -Command 'protoc' -Arguments @(
        "--proto_path=$protoDirectory",
        "--dart_out=grpc:$tempRoot",
        $protoPath
    )

    $actualNames = @(Get-ChildItem -LiteralPath $tempRoot -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($tempRoot, $_.FullName).Replace('\', '/')
    } | Sort-Object)
    $expectedNames = @($rawNames | Sort-Object)
    $differences = @(Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames)
    if ($differences.Count -ne 0) {
        throw "The Dart generator produced an unexpected output set."
    }

    Remove-Item -LiteralPath (Join-Path $tempRoot 'ui.pbjson.dart')
    $generatedPaths = @($trackedNames | ForEach-Object { Join-Path $tempRoot $_ })
    foreach ($path in $generatedPaths) {
        Write-NormalizedDart -Path $path -StripComments $true
    }
    $null = Invoke-CheckedTool -Command 'dart' -Arguments (@('format') + $generatedPaths)
    foreach ($path in $generatedPaths) {
        Write-NormalizedDart -Path $path -StripComments $false
    }

    $postProcessNames = @(Get-ChildItem -LiteralPath $tempRoot -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($tempRoot, $_.FullName).Replace('\', '/')
    } | Sort-Object)
    $expectedTrackedNames = @($trackedNames | Sort-Object)
    $postProcessDifferences = @(Compare-Object -ReferenceObject $expectedTrackedNames -DifferenceObject $postProcessNames)
    if ($postProcessDifferences.Count -ne 0) {
        throw "The processed Dart output set is invalid."
    }

    foreach ($name in $trackedNames) {
        $generatedPath = Join-Path $tempRoot $name
        $destinationPath = Join-Path $destinationDirectory $name
        $generatedBytes = [IO.File]::ReadAllBytes($generatedPath)
        if ($Check) {
            if (-not [IO.File]::Exists($destinationPath)) {
                throw "$name is not tracked."
            }
            $destinationBytes = [IO.File]::ReadAllBytes($destinationPath)
            if (-not (Test-EqualBytes -Left $generatedBytes -Right $destinationBytes)) {
                throw "$name is not byte-clean."
            }
        } else {
            [IO.File]::WriteAllBytes($destinationPath, $generatedBytes)
        }
    }
} finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    if (-not $resolvedTempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Split-Path -Leaf $resolvedTempRoot).StartsWith('digitalbrain-ui-proto-', [StringComparison]::Ordinal)) {
        throw "The temporary cleanup path is invalid."
    }
    if (Test-Path -LiteralPath $resolvedTempRoot) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}

if ($Check) {
    Write-Output 'The generated Dart UI client is byte-clean.'
} else {
    Write-Output 'Generated the Dart UI client.'
}
