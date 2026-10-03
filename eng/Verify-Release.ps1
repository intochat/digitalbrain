param(
    [Parameter(Mandatory)][string]$Feed,
    [string]$Output = (Join-Path ([IO.Path]::GetTempPath()) ("digitalbrain-release-" + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$Feed = (Resolve-Path -LiteralPath $Feed).Path
if (Test-Path -LiteralPath $Output) { throw 'Choose a fresh release output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$Output = (Resolve-Path -LiteralPath $Output).Path
# Consumer projects must not inherit repository or machine-local build customizations.
foreach ($buildFile in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
    '<Project />' | Set-Content -LiteralPath (Join-Path $Output $buildFile)
}

$repo = Split-Path $PSScriptRoot -Parent
$revision = 'e39ecb35df23b80ef2f9340ea80a8bd6c8f8e3a5'
$initialHashes = @(Get-ChildItem -LiteralPath $Feed -Filter '*.nupkg' | Sort-Object Name | ForEach-Object {
    [ordered]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
& (Join-Path $PSScriptRoot 'Verify-NuGetConsumers.ps1') -Feed $Feed -Output (Join-Path $Output 'consumers')

# Export only the pinned legacy dependency closure. Never alter the user's checkout.
$legacy = Join-Path $Output 'legacy'
New-Item -ItemType Directory -Path $legacy | Out-Null
$archive = Join-Path $Output 'legacy.tar'
$paths = @('Directory.Build.props', 'Directory.Packages.props', 'global.json',
    'src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity',
    'src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity.Contracts',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Core',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Client',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Secrets/MasterKeyWrapper.cs',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Secrets/MasterKeyOptions.cs',
    'src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Secrets/IKeyWrapper.cs')
git -C $repo archive --format=tar -o $archive $revision -- @paths
if ($LASTEXITCODE -ne 0) { throw "Cannot export pinned legacy revision $revision. Fetch its history first." }
tar -xf $archive -C $legacy
if ($LASTEXITCODE -ne 0) { throw 'Legacy archive extraction failed.' }
$identity = Join-Path $legacy 'src/Modules/DigitalBrain/Identity/DigitalBrain.Modules.Identity'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReleaseRehearsal/Legacy.cs') -Destination (Join-Path $identity 'Rehearsal.cs')
foreach ($name in @('MasterKeyWrapper.cs', 'MasterKeyOptions.cs', 'IKeyWrapper.cs')) {
    Copy-Item -LiteralPath (Join-Path $legacy "src/Modules/DigitalBrain/Kernel/DigitalBrain.Platform/Secrets/$name") -Destination $identity
}
$legacyProject = Join-Path $identity 'DigitalBrain.Modules.Identity.csproj'
[xml]$project = Get-Content -LiteralPath $legacyProject -Raw
$group = $project.CreateElement('PropertyGroup')
$outputType = $project.CreateElement('OutputType'); $outputType.InnerText = 'Exe'; $null = $group.AppendChild($outputType)
$null = $project.Project.AppendChild($group)
$group = $project.CreateElement('ItemGroup')
$reference = $project.CreateElement('PackageReference'); $reference.SetAttribute('Include', 'Microsoft.Orleans.Persistence.AzureStorage')
$null = $group.AppendChild($reference); $null = $project.Project.AppendChild($group)
$project.Save($legacyProject)
dotnet build $legacyProject -c Release -p:RunAnalyzers=false -p:CodeGraphRefresh=false > (Join-Path $Output 'legacy-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw "Legacy build failed: $Output/legacy-build.log" }

$candidate = Join-Path $Output 'consumers/Rehearsal'
New-Item -ItemType Directory -Path $candidate | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ReleaseRehearsal/Program.cs'), (Join-Path $PSScriptRoot 'ReleaseRehearsal/Worker.cs') -Destination $candidate
[xml]$all = Get-Content -LiteralPath (Join-Path $Output 'consumers/All/All.csproj') -Raw
$version = ($all.Project.ItemGroup.PackageReference | Where-Object Include -EQ 'DigitalBrain.Testing.E2E').Version
[xml]$versions = Get-Content -LiteralPath (Join-Path $repo 'Directory.Packages.props') -Raw
$aspireVersion = ($versions.Project.ItemGroup.PackageVersion | Where-Object Include -EQ 'Aspire.Hosting.Testing').Version
$cache = [Security.SecurityElement]::Escape((Join-Path $Output 'consumers/packages'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AspireOrchestrationVersion>$aspireVersion</AspireOrchestrationVersion><RestorePackagesPath>$cache</RestorePackagesPath></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DigitalBrain.Testing.E2E" Version="$version" />
    <PackageReference Include="DigitalBrain.Aspire.Client" Version="$version" />
    <PackageReference Include="Aspire.Hosting.Orchestration.`$(NETCoreSdkPortableRuntimeIdentifier)" Version="$aspireVersion" ExcludeAssets="all" />
    <PackageReference Include="Aspire.Dashboard.Sdk.`$(NETCoreSdkPortableRuntimeIdentifier)" Version="$aspireVersion" ExcludeAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $candidate 'Rehearsal.csproj')
dotnet build (Join-Path $candidate 'Rehearsal.csproj') -c Release > (Join-Path $Output 'candidate-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw "Candidate build failed: $Output/candidate-build.log" }
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.ArgumentList.Add((Join-Path $candidate 'bin/Release/net11.0/Rehearsal.dll'))
$start.Environment['REHEARSAL_OUTPUT'] = $Output
$start.Environment['REHEARSAL_LEGACY'] = Join-Path $identity 'bin/Release/net11.0/DigitalBrain.Modules.Identity.dll'
$process = [Diagnostics.Process]::Start($start)
try {
    if (!$process.WaitForExit(900000)) { $process.Kill($true); throw 'Release rehearsal exceeded 15 minutes.' }
    if ($process.ExitCode -ne 0) { throw "Release rehearsal failed. Logs and retained volume name: $Output" }
} finally { $process.Dispose() }
$finalHashes = @(Get-ChildItem -LiteralPath $Feed -Filter '*.nupkg' | Sort-Object Name | ForEach-Object {
    [ordered]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
if (($initialHashes | ConvertTo-Json -Compress) -ne ($finalHashes | ConvertTo-Json -Compress)) { throw 'Package artifacts changed during verification.' }
if (!(Test-Path -LiteralPath (Join-Path $Output 'rehearsal-passed.json'))) { throw 'Missing rehearsal evidence.' }
[ordered]@{ LegacyRevision = $revision; Packages = $finalHashes; VerifiedAt = [DateTimeOffset]::UtcNow.ToString('O') } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'verified-packages.json')
Write-Host "Release verification passed. Package manifest: $Output/verified-packages.json"
