[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$packages = Join-Path $repository $Output

Push-Location $repository

try {
    if (Test-Path $packages) {
        Remove-Item -Recurse -Force $packages
    }

    dotnet build-server shutdown | Out-Null

    dotnet pack "$repository/DigitalBrain.slnx" -c $Configuration -o $packages
    if ($LASTEXITCODE -ne 0) { throw "pack failed with exit code $LASTEXITCODE" }

    $produced = Get-ChildItem $packages -Filter *.nupkg
    $symbols = Get-ChildItem $packages -Filter *.snupkg
    if ($produced.Count -ne $symbols.Count) {
        throw "every package must ship symbols: $($produced.Count) packages, $($symbols.Count) symbol packages"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $providerSdkPrefixes = @('Anthropic', 'OpenAI', 'Microsoft.Extensions.AI', 'OllamaSharp')
    $providerOwner = 'DigitalBrain.Modules.AI'
    $consumerPathPackages = @(
        'DigitalBrain', 'DigitalBrain.Abstractions', 'DigitalBrain.Client',
        'DigitalBrain.Aspire', 'DigitalBrain.Aspire.Hosting',
        'DigitalBrain.Modules.AI.Contracts')
    $packagesThatMayHostNeurons = @('DigitalBrain.Testing', 'DigitalBrain.DevTools')

    $declaredDependencies = @{}

    foreach ($package in $produced) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)

        try {
            $manifest = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' }
            $metadata = ([xml](New-Object System.IO.StreamReader($manifest.Open())).ReadToEnd()).package.metadata
            $declaredDependencies[$metadata.id] = @($metadata.dependencies.group.dependency | ForEach-Object { $_.id })
        }
        finally {
            $archive.Dispose()
        }
    }

    $reachableFrom = {
        param($origin)

        $reached = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        $pending = [System.Collections.Generic.Queue[string]]::new()
        $pending.Enqueue($origin)

        while ($pending.Count -gt 0) {
            foreach ($dependency in $declaredDependencies[$pending.Dequeue()]) {
                if ($reached.Add($dependency) -and $declaredDependencies.ContainsKey($dependency)) {
                    $pending.Enqueue($dependency)
                }
            }
        }

        $reached
    }

    $isProviderSdk = { param($id) $providerSdkPrefixes | Where-Object { $id.StartsWith($_, [System.StringComparison]::Ordinal) } }
    $breaches = @()

    foreach ($identity in $consumerPathPackages) {
        if (-not $declaredDependencies.ContainsKey($identity)) {
            $breaches += "$identity was not produced, so its boundary was never verified"
            continue
        }

        $reachable = & $reachableFrom $identity

        foreach ($sdk in $reachable | Where-Object { & $isProviderSdk $_ }) {
            $breaches += "$identity can reach the provider SDK $sdk; provider SDKs and credentials live only in $providerOwner"
        }

        foreach ($forbidden in @('DigitalBrain.Kernel', 'DigitalBrain.Testing') | Where-Object { $reachable.Contains($_) }) {
            $breaches += "$identity can reach $forbidden, which is forbidden on the consumer path"
        }
    }

    foreach ($identity in $packagesThatMayHostNeurons) {
        foreach ($sdk in $declaredDependencies[$identity] | Where-Object { & $isProviderSdk $_ }) {
            $breaches += "$identity declares the provider SDK $sdk itself; only $providerOwner may"
        }
    }

    foreach ($identity in $declaredDependencies.Keys | Where-Object { $_ -ne $providerOwner }) {
        foreach ($sdk in $declaredDependencies[$identity] | Where-Object { & $isProviderSdk $_ }) {
            $breaches += "$identity declares the provider SDK $sdk itself; only $providerOwner may"
        }
    }

    if ($breaches) {
        throw "the packaged security boundary is breached:`n$($breaches -join "`n")"
    }

    $checksums = $produced | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.Name)"
    }

    Set-Content -Path (Join-Path $packages 'SHA256SUMS.txt') -Value $checksums -Encoding utf8

    "packed $($produced.Count) packages and $($symbols.Count) symbol packages into $packages"
}
finally {
    Pop-Location
}
