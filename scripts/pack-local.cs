#!/usr/bin/env -S dotnet --
// Packs the eighteen DigitalBrain packages as <VERSION>-preview.local.<yyyyMMddHHmmss> into a flat local feed:
// `dotnet run scripts/pack-local.cs -- --feed <folder>`
using System.Diagnostics;
using System.Globalization;

const string Usage = "Usage: dotnet run scripts/pack-local.cs -- --feed <folder>";

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string is { } entry
    ? Path.GetFullPath(Path.Combine(entry, ".."))
    : Directory.GetCurrentDirectory();

string? feed = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--feed" when i + 1 < args.Length:
            feed = args[++i];
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 1;
    }
}

if (string.IsNullOrWhiteSpace(feed) || feed.StartsWith('-'))
{
    Console.Error.WriteLine(Usage);
    return 1;
}

var feedPath = Path.GetFullPath(feed);
Directory.CreateDirectory(feedPath);

var baseVersion = File.ReadAllText(Path.Combine(root, "VERSION")).Trim();
var suffix = $"preview.local.{DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
var fullVersion = $"{baseVersion}-{suffix}";

string[] projects =
[
    "src/DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj",
    "src/DigitalBrain.UI/DigitalBrain.UI.csproj",
    "src/DigitalBrain/DigitalBrain.csproj",
    "src/DigitalBrain.Capabilities.Storage/DigitalBrain.Capabilities.Storage.csproj",
    "src/DigitalBrain.Capabilities.Secrets/DigitalBrain.Capabilities.Secrets.csproj",
    "src/DigitalBrain.Capabilities.Identity/DigitalBrain.Capabilities.Identity.csproj",
    "src/DigitalBrain.Capabilities.Compute/DigitalBrain.Capabilities.Compute.csproj",
    "src/DigitalBrain.Capabilities.Connections/DigitalBrain.Capabilities.Connections.csproj",
    "src/DigitalBrain.Identity.Local/DigitalBrain.Identity.Local.csproj",
    "src/DigitalBrain.Graph/DigitalBrain.Graph.csproj",
    "src/DigitalBrain.Apps.Packaging/DigitalBrain.Apps.Packaging.csproj",
    "src/DigitalBrain.Runtime.Http/DigitalBrain.Runtime.Http.csproj",
    "src/DigitalBrain.Runtime.Apps/DigitalBrain.Runtime.Apps.csproj",
    "src/DigitalBrain.Testing/DigitalBrain.Testing.csproj",
    "src/DigitalBrain.Runtime.Orleans/DigitalBrain.Runtime.Orleans.csproj",
    "src/DigitalBrain.Runtime.Orleans.Azure/DigitalBrain.Runtime.Orleans.Azure.csproj",
    "src/DigitalBrain.Testing.Silo/DigitalBrain.Testing.Silo.csproj",
    "templates/DigitalBrain.Templates/DigitalBrain.Templates.csproj",
];

foreach (var relativeProject in projects)
{
    var projectPath = Path.Combine(root, relativeProject);
    var startInfo = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("pack");
    startInfo.ArgumentList.Add(projectPath);
    startInfo.ArgumentList.Add("-c");
    startInfo.ArgumentList.Add("Release");
    startInfo.ArgumentList.Add("-o");
    startInfo.ArgumentList.Add(feedPath);
    startInfo.ArgumentList.Add($"-p:VersionSuffix={suffix}");

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        Console.Error.WriteLine($"Failed to start dotnet pack for {relativeProject}.");
        return 1;
    }

    await process.WaitForExitAsync().ConfigureAwait(false);
    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine($"dotnet pack failed for {relativeProject} (exit {process.ExitCode}).");
        return process.ExitCode;
    }
}

Console.WriteLine($"Packed {fullVersion} into {feedPath}:");
foreach (var package in Directory.EnumerateFiles(feedPath, $"*.{fullVersion}.nupkg").Order(StringComparer.Ordinal))
{
    Console.WriteLine($"  {Path.GetFileName(package)}");
}

return 0;
