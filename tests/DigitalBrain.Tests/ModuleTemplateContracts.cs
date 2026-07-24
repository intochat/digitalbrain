using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleTemplateContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();
    private static readonly string[] QuickstartTestReferences =
    [
        "PackageReference:Microsoft.NET.Test.Sdk",
        "PackageReference:xunit.runner.visualstudio",
        "PackageReference:xunit.v3",
        "ProjectReference:DigitalBrain.Quickstart",
        "ProjectReference:DigitalBrain.Quickstart.Contracts",
        "ProjectReference:DigitalBrain.Testing",
    ];
    private static readonly string[] QuickstartTestForbiddenVocabulary =
    [
        "Orleans",
        "Aspire",
        "IGrainFactory",
        "GrainId",
        "NeuronId",
        "Simulation",
        "Scenario",
        "ConcurrentDictionary",
        "DisableTestParallelization",
        "CA1062",
    ];

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

    [Fact(DisplayName = "Quickstart separates leaf contracts from its packable runtime")]
    public void QuickstartSeparatesLeafContractsFromItsPackableRuntime()
    {
        var samples = Path.Combine(RepositoryRoot, "samples");
        var contracts = Path.Combine(
            samples,
            "DigitalBrain.Quickstart.Contracts",
            "DigitalBrain.Quickstart.Contracts.csproj");
        var runtime = Path.Combine(
            samples,
            "DigitalBrain.Quickstart",
            "DigitalBrain.Quickstart.csproj");
        var violations = new List<string>();

        ValidateQuickstartProject(
            contracts,
            ["DigitalBrain.Abstractions"],
            [
                "DigitalBrain.Kernel",
                "Microsoft.Orleans.Server",
                "Aspire",
                "DigitalBrain.Client",
                "DigitalBrain.DevTools",
                "DigitalBrain.Testing",
            ],
            requireLibraryShape: false,
            violations);
        ValidateQuickstartProject(
            runtime,
            [
                "DigitalBrain.Kernel",
                "DigitalBrain.Quickstart.Contracts",
            ],
            [
                "Microsoft.Orleans.Server",
                "Aspire",
                "DigitalBrain.Client",
                "DigitalBrain.DevTools",
                "DigitalBrain.Testing",
            ],
            requireLibraryShape: true,
            violations);

        var solution = File.ReadAllText(
            Path.Combine(RepositoryRoot, "DigitalBrain.slnx"));
        foreach (var project in new[]
                 {
                     "samples/DigitalBrain.Quickstart.Contracts/DigitalBrain.Quickstart.Contracts.csproj",
                     "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.csproj",
                 })
        {
            if (!solution.Contains(project, StringComparison.Ordinal))
            {
                violations.Add($"DigitalBrain.slnx does not include {project}.");
            }
        }

        var aspireHosting = Directory.EnumerateFiles(
                samples,
                "DigitalBrain.Quickstart.Aspire.Hosting.csproj",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();
        if (aspireHosting.Length != 0)
        {
            violations.Add(
                $"Quickstart must not create an Aspire.Hosting project: {string.Join(", ", aspireHosting)}.");
        }

        if (violations.Count != 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, violations));
        }
    }

    [Fact(DisplayName = "Quickstart tests use only the public testing surface")]
    public void QuickstartTestsUseOnlyThePublicTestingSurface()
    {
        var projectDirectory = Path.Combine(
            RepositoryRoot,
            "tests",
            "DigitalBrain.Quickstart.Tests");
        var project = Path.Combine(
            projectDirectory,
            "DigitalBrain.Quickstart.Tests.csproj");
        var fixture = Path.Combine(
            projectDirectory,
            "QuickstartFixture.cs");
        var violations = new List<string>();

        if (!File.Exists(project))
        {
            Assert.Fail(
                $"{Path.GetRelativePath(RepositoryRoot, project)} does not exist.");
        }

        var document = XDocument.Load(project);
        var references = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(reference =>
                $"{reference.Name.LocalName}:{ReferenceName(reference)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (!references.SequenceEqual(
                QuickstartTestReferences,
                StringComparer.Ordinal))
        {
            violations.Add(
                $"{Path.GetRelativePath(RepositoryRoot, project)} directly references "
                + $"[{string.Join(", ", references)}], expected "
                + $"[{string.Join(", ", QuickstartTestReferences)}].");
        }

        if (!string.Equals(
                document.Descendants("OutputType").SingleOrDefault()?.Value,
                "Exe",
                StringComparison.Ordinal))
        {
            violations.Add(
                $"{Path.GetRelativePath(RepositoryRoot, project)} must be an executable test project.");
        }

        var inspectedFiles = Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(projectDirectory, path))
            .Append(project);

        foreach (var path in inspectedFiles)
        {
            var source = File.ReadAllText(path);
            foreach (var token in QuickstartTestForbiddenVocabulary)
            {
                if (source.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(RepositoryRoot, path)} contains forbidden token '{token}'.");
                }
            }
        }

        if (!File.Exists(fixture))
        {
            violations.Add(
                $"{Path.GetRelativePath(RepositoryRoot, fixture)} does not exist.");
        }
        else
        {
            const string moduleSelection =
                "brain.AddModule<QuickstartModule>()";
            var selectionCount = CountOccurrences(
                File.ReadAllText(fixture),
                moduleSelection);
            if (selectionCount != 1)
            {
                violations.Add(
                    $"{Path.GetRelativePath(RepositoryRoot, fixture)} must contain exactly one "
                    + $"{moduleSelection} selection; found {selectionCount}.");
            }
        }

        if (violations.Count != 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, violations));
        }
    }

    private static void ValidateQuickstartProject(
        string path,
        string[] expectedConsumerDependencies,
        string[] forbiddenDependencies,
        bool requireLibraryShape,
        List<string> violations)
    {
        if (!File.Exists(path))
        {
            violations.Add(
                $"{Path.GetRelativePath(RepositoryRoot, path)} does not exist.");
            return;
        }

        var document = XDocument.Load(path);
        var relativePath = Path.GetRelativePath(RepositoryRoot, path);
        var packageReferences = document.Descendants("PackageReference")
            .ToArray();
        var projectReferences = document.Descendants("ProjectReference")
            .ToArray();
        var consumerDependencies = packageReferences
            .Concat(projectReferences)
            .Where(FlowsToConsumers)
            .Select(ReferenceName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (!consumerDependencies.SequenceEqual(
                expectedConsumerDependencies.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            violations.Add(
                $"{relativePath} flows [{string.Join(", ", consumerDependencies)}], "
                + $"expected [{string.Join(", ", expectedConsumerDependencies.Order(StringComparer.Ordinal))}].");
        }

        var generatorReferences = projectReferences
            .Where(reference => string.Equals(
                ReferenceName(reference),
                "DigitalBrain.SourceGeneration",
                StringComparison.Ordinal))
            .ToArray();
        if (generatorReferences.Length != 1)
        {
            violations.Add(
                $"{relativePath} must reference DigitalBrain.SourceGeneration exactly once.");
        }
        else
        {
            var generator = generatorReferences[0];
            if (!string.Equals(
                    (string?)generator.Attribute("OutputItemType"),
                    "Analyzer",
                    StringComparison.Ordinal)
                || !string.Equals(
                    (string?)generator.Attribute("ReferenceOutputAssembly"),
                    "false",
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    (string?)generator.Attribute("PrivateAssets"),
                    "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"{relativePath} must keep DigitalBrain.SourceGeneration analyzer-only and private.");
            }
        }

        var allDependencies = packageReferences
            .Concat(projectReferences)
            .Select(ReferenceName)
            .ToArray();
        foreach (var forbidden in forbiddenDependencies)
        {
            if (allDependencies.Any(dependency =>
                    dependency.Contains(
                        forbidden,
                        StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(
                    $"{relativePath} must not depend on {forbidden}.");
            }
        }

        if (!string.Equals(
                document.Descendants("IsPackable").SingleOrDefault()?.Value,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"{relativePath} must be packable.");
        }

        if (!requireLibraryShape)
        {
            return;
        }

        if (document.Descendants("OutputType").Any())
        {
            violations.Add($"{relativePath} must not declare OutputType.");
        }

        var directlyVersioned = packageReferences
            .Where(reference =>
                reference.Attribute("Version") is not null
                || reference.Elements("Version").Any())
            .Select(ReferenceName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (directlyVersioned.Length != 0)
        {
            violations.Add(
                $"{relativePath} directly versions [{string.Join(", ", directlyVersioned)}].");
        }
    }

    private static bool IsBuildOutput(string root, string path)
        => Path.GetRelativePath(root, path)
            .Split(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;

        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)
        && !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

    private static string ReferenceName(XElement reference) =>
        reference.Name.LocalName == "PackageReference"
            ? IncludeOf(reference)
            : Path.GetFileNameWithoutExtension(
                IncludeOf(reference).Replace('\\', '/'));

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
