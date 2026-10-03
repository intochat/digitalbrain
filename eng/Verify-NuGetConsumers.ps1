param(
    [Parameter(Mandatory)][string]$Feed,
    [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) ("digitalbrain-consumers-" + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$Feed = (Resolve-Path -LiteralPath $Feed).Path
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$Output = (Resolve-Path -LiteralPath $Output).Path
# Consumer projects must not inherit repository or machine-local build customizations.
foreach ($buildFile in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    '<Project />' | Set-Content -LiteralPath (Join-Path $Output $buildFile)
}

$cache = Join-Path $Output 'packages'
if (Test-Path -LiteralPath $cache) { throw 'Choose a fresh output directory: the package cache must be empty.' }
$externalCache = ((dotnet nuget locals global-packages --list) -split ':', 2)[1].Trim()
$packages = @{}
Get-ChildItem -LiteralPath $Feed -Filter '*.nupkg' | ForEach-Object {
    $zip = [IO.Compression.ZipFile]::OpenRead($_.FullName)
    try {
        $entry = $zip.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($packages.ContainsKey($spec.package.metadata.id)) { throw "The feed contains multiple versions of $($spec.package.metadata.id). Use one release train per feed." }
        $packages[$spec.package.metadata.id] = $spec.package.metadata.version
    } finally { $zip.Dispose() }
}
if ($packages.Count -eq 0) { throw 'The feed contains no packages.' }
$escapedFeed = [Security.SecurityElement]::Escape($Feed)
$escapedCache = [Security.SecurityElement]::Escape($externalCache)
@"
<configuration>
  <packageSources><clear/><add key="built" value="$escapedFeed"/><add key="external-cache" value="$escapedCache"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping>
    <packageSource key="built"><package pattern="DigitalBrain*"/></packageSource>
    <packageSource key="external-cache"><package pattern="*"/></packageSource>
    <packageSource key="nuget"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $Output 'NuGet.Config')
$profiles = @{
    Script = @('DigitalBrain.Client')
    Kernel = @('DigitalBrain.Kernel')
    AppHost = @($packages.Keys | Where-Object { $_ -like '*.Aspire.Hosting' })
    Server = @('DigitalBrain.Aspire.Server', 'DigitalBrain.Platform')
    All = @($packages.Keys)
}
$code = @{
    Script = 'public class Consumer { public Task<DigitalBrain.Client.DigitalBrainConnection> Connect(string[] args) => DigitalBrain.Client.DigitalBrainClient.ConnectAsync(args); }'
    Kernel = 'public class Consumer { public Type Runtime => typeof(DigitalBrain.Kernel.Neuron); }'
    AppHost = 'using Aspire.Hosting; using DigitalBrain.Aspire.Hosting; public class Consumer { public DigitalBrainBuilder Compose(IDistributedApplicationBuilder builder) => builder.AddDigitalBrain("brain"); }'
    Server = 'using DigitalBrain.Aspire.Server; using DigitalBrain.Platform; using Microsoft.Extensions.Hosting; public class Consumer { public void Compose(IHostApplicationBuilder builder) { builder.AddDigitalBrainServer(_ => {}); builder.AddDigitalBrainPlatform(); } }'
    All = 'public class Consumer {}'
}
foreach ($profile in $profiles.Keys | Sort-Object) {
    $directory = Join-Path $Output $profile
    New-Item -ItemType Directory -Path $directory | Out-Null
    $references = foreach ($id in $profiles[$profile] | Sort-Object) {
        if (!$packages.ContainsKey($id)) { throw "Missing package: $id" }
        '<PackageReference Include="{0}" Version="{1}" />' -f $id, $packages[$id]
    }
    $escapedFreshCache = [Security.SecurityElement]::Escape($cache)
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableNETAnalyzers>false</EnableNETAnalyzers><RestorePackagesPath>$escapedFreshCache</RestorePackagesPath></PropertyGroup>
  <ItemGroup>$($references -join "`n")</ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory "$profile.csproj")
    $code[$profile] | Set-Content -LiteralPath (Join-Path $directory 'Consumer.cs')
    dotnet build (Join-Path $directory "$profile.csproj") -c Release --nologo > (Join-Path $Output "$profile.log") 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Consumer $profile failed. See $Output/$profile.log" }
    $assets = Get-Content -LiteralPath (Join-Path $directory 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $ids = @($assets.libraries.PSObject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
    $forbidden = switch ($profile) {
        Script { '^(Azure\.|Aspire\.|OpenTelemetry\.|Microsoft.Extensions.Hosting|Microsoft.Orleans.(Client|Server|Runtime)|DigitalBrain.(Kernel|Platform))' }
        Kernel { '^DigitalBrain.(Client|Platform)' }
        AppHost { '^DigitalBrain.Kernel$|^DigitalBrain.Platform$|^DigitalBrain.Modules.*(?<!Aspire.Hosting)(?<!Contracts)$' }
        default { $null }
    }
    if ($forbidden -and ($ids | Where-Object { $_ -match $forbidden })) { throw "Forbidden dependency in $profile`: $($ids | Where-Object { $_ -match $forbidden })" }
    Write-Host "$profile consumer passed."
}
Write-Host "Verified $($packages.Count) packages. Logs: $Output"
