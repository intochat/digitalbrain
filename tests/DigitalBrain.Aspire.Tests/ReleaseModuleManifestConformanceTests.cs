using System.Globalization;
using System.Xml.Linq;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

public sealed class ReleaseModuleManifestConformanceTests
{
    private const string ModuleKeyPrefix = "DigitalBrain__Modules__";
    private const string DockerfilePath = "src/Kernel/DigitalBrain.Silo/Dockerfile";
    private const string PublishProfilePath = "src/Kernel/DigitalBrain.Silo/Properties/PublishProfiles/Container.pubxml";

    private static readonly string[] ExpectedModules =
    [
        "DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI",
        "DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory",
        "DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time",
        "DigitalBrain.Execution.ExecutionModule, DigitalBrain.Modules.Execution",
        "DigitalBrain.Google.GoogleModule, DigitalBrain.Modules.Google",
        "DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce",
        "DigitalBrain.UI.UIModule, DigitalBrain.Modules.UI",
    ];

    [Theory]
    [InlineData(DockerfilePath)]
    [InlineData(PublishProfilePath)]
    public void ReleaseManifestBootsWithTheAppHostModuleSet(string relativePath)
    {
        var entries = relativePath.EndsWith(".pubxml", StringComparison.Ordinal)
            ? ReadPublishProfile(relativePath)
            : ReadDockerfile(relativePath);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(static entry =>
                KeyValuePair.Create<string, string?>(
                    $"{DigitalBrainNames.Modules}:{entry.Index}",
                    entry.Name)))
            .Build();

        var manifest = ModuleManifest.FromConfiguration(configuration);

        Assert.Equal(Enumerable.Range(0, ExpectedModules.Length), entries.Select(static entry => entry.Index));
        Assert.Equal(
            ExpectedModules,
            manifest.Types.Select(static type => $"{type.FullName}, {type.Assembly.GetName().Name}"));
    }

    private static (int Index, string Name)[] ReadDockerfile(string relativePath)
    {
        return File.ReadLines(RepositoryFile(relativePath))
            .Select(static line => line.Trim().TrimEnd('\\').TrimEnd())
            .Where(static line => line.StartsWith(ModuleKeyPrefix, StringComparison.Ordinal))
            .Select(static assignment =>
            {
                var separator = assignment.IndexOf('=');
                if (separator < 0)
                {
                    throw new InvalidDataException($"Invalid module assignment '{assignment}'.");
                }

                return ParseEntry(
                    assignment[..separator],
                    assignment[(separator + 1)..].Trim('"'));
            })
            .OrderBy(static entry => entry.Index)
            .ToArray();
    }

    private static (int Index, string Name)[] ReadPublishProfile(string relativePath)
    {
        return XDocument.Load(RepositoryFile(relativePath))
            .Descendants()
            .Where(static element => element.Name.LocalName == "ContainerEnvironmentVariable")
            .Select(static element => (
                Key: (string?)element.Attribute("Include"),
                Value: (string?)element.Attribute("Value")))
            .Where(static entry => entry.Key?.StartsWith(ModuleKeyPrefix, StringComparison.Ordinal) is true)
            .Select(static entry => ParseEntry(
                entry.Key ?? throw new InvalidDataException("Module entry has no key."),
                entry.Value ?? throw new InvalidDataException("Module entry has no value.")))
            .OrderBy(static entry => entry.Index)
            .ToArray();
    }

    private static (int Index, string Name) ParseEntry(string key, string name)
    {
        if (!int.TryParse(
                key.AsSpan(ModuleKeyPrefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index))
        {
            throw new InvalidDataException($"Invalid module key '{key}'.");
        }

        return (index, name);
    }

    private static string RepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }
        }

        throw new DirectoryNotFoundException("Could not locate the DigitalBrain repository root.");
    }
}
