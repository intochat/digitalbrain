using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleTemplateContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName = "module .Contracts packages are leaves with only approved public vocabulary dependencies")]
    public void ModuleContractsPackagesAreLeaves()
    {
        var contractsProjects = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "modules"), "*.Contracts.csproj", SearchOption.AllDirectories)
            .ToList();

        Assert.NotEmpty(contractsProjects);

        foreach (var project in contractsProjects)
        {
            var document = XDocument.Load(project);
            var projectName = Path.GetFileNameWithoutExtension(project);
            var packageRefs = document.Descendants("PackageReference")
                .Where(FlowsToConsumers)
                .Select(IncludeOf)
                .ToList();

            var projectRefs = document.Descendants("ProjectReference")
                .Where(FlowsToConsumers)
                .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')))
                .ToList();
            var expectedPackages = projectName == "DigitalBrain.Modules.AI.Contracts"
                ? new[] { "Microsoft.Extensions.AI.Abstractions" }
                : [];
            var expectedProjects = projectName == "DigitalBrain.Modules.AI.Contracts"
                ? new[] { "DigitalBrain.Abstractions", "DigitalBrain.Modules.Tasks.Contracts" }
                : ["DigitalBrain.Abstractions"];

            Assert.Equal(expectedPackages, packageRefs.Order(StringComparer.Ordinal));
            Assert.Equal(expectedProjects, projectRefs.Order(StringComparer.Ordinal));
        }
    }

    [Fact(DisplayName = "a deliberately wrong contracts project fails the leaf guard")]
    public void DeliberatelyWrongContractsProjectIsDetected()
    {
        var fixture = Path.Combine(RepositoryRoot, "tests", "fixtures", "WrongModule.Contracts.csproj");
        Assert.True(File.Exists(fixture), "fixture WrongModule.Contracts.csproj must exist");

        var document = XDocument.Load(fixture);
        var packageRefs = document.Descendants("PackageReference")
            .Where(FlowsToConsumers)
            .Select(IncludeOf)
            .ToList();

        Assert.Contains(packageRefs, name => name.StartsWith("OpenAI", StringComparison.Ordinal));
    }

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)
        && !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

    private static string IncludeOf(XElement reference) =>
        reference.Attribute("Include")?.Value
        ?? throw new InvalidOperationException($"A {reference.Name.LocalName} element carries no Include attribute.");

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
