using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ArchitectureCutContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] RejectedHostingMethodNames =
    [
        "Add" + "Brain",
        "WithAzure" + "Storage",
        "WithDevelopment" + "Stores",
    ];

    private static readonly string[] RejectedHostingTypeNames =
    [
        "Brain" + "Service",
        "BrainModule" + "Hosting",
    ];

    private static readonly string[] RejectedCompositionSourceIdentifiers =
    [
        "ConditionalWeakTable<" + "IModule",
        "ModuleSerialization" + "Method",
        "GetMembers(\"" + "Configure",
        "DigitalBrain.Generated." + "ModuleCatalog",
    ];

    private static readonly string[] RejectedIdentifiers =
    [
        "Model" + "Tier",
        "Model" + "Providers",
        "IModel" + "CompletionService",
        "Ask" + "ModelAsync",
        "AddDigitalBrain" + "Models",
        "AddAI" + "Module",
        "Chat" + "ModelNeuron",
        "Model" + "Descriptor",
        "Model" + "Catalog",
        "Provider" + "Factory",
        "ILlm" + "Definition",
        "Module" + "Descriptor",
        "Module" + "Composition",
        "Module" + "Wiring",
    ];

    private static readonly HashSet<string> RejectedTestingTypeNames =
    [
        "Simu" + "lation",
        "Simu" + "lations",
        "Scen" + "ario",
        "Simu" + "lationCluster",
        "Scen" + "arioClock",
        "Scen" + "arioStages",
        "Simu" + "lationAssertionException",
        "ISimu" + "lationNeuron",
        "IBeh" + "avior",
        "IBeh" + "aviorTest",
        "Beh" + "aviorFixture",
        "Fault" + "Point",
        "Fault" + "Handle",
        "Scen" + "arioFailureArtifact",
        "Hosted" + "Application",
        "Hosted" + "Scenario",
    ];

    private static readonly string[] RejectedHostedTestingSourcePatterns =
    [
        @"\bHosted" + @"Application\b",
        @"\bHosted" + @"Scenario\b",
        @"\bDefaultTracked" + @"ProcessNames\b",
        @"\bGetProcesses" + @"ByName\b",
        @"\bIsExclusive" + @"Held\b",
        @"\bExclusive" + @"Owner\b",
        @"\bpublic\s+Distributed" + @"Application\b",
    ];

    private static readonly string[] RejectedTestingSourcePatterns =
    [
        @"\bSimu" + @"lation\b",
        @"\bSimu" + @"lations\b",
        @"\bScen" + @"ario\b",
        @"\bSimu" + "lationCluster\b",
        @"\bScen" + "arioClock\b",
        @"\bScen" + "arioStages\b",
        @"\bScen" + "arioFaults\b",
        @"\bScen" + "arioFailureArtifact\b",
        @"\bSimu" + "lationAssertionException\b",
        @"\bSimu" + "lationNeuron\b",
        @"\bISimu" + "lationNeuron\b",
        @"\bNeuron" + "Catalog\b",
        @"\bSynapse" + "Observer\b",
        @"\bIBeh" + "avior\b",
        @"\bIBeh" + "aviorTest\b",
        @"\bBeh" + "aviorFixture\b",
        @"\bFault" + "Point\b",
        @"\bFault" + "Handle\b",
    ];

    [Fact(DisplayName = "a module is a marker, not a second configuration language")]
    public void ModuleIsAMarker()
        => Assert.Empty(typeof(IModule).GetMembers(BindingFlags.Instance | BindingFlags.Public));

    [Fact(DisplayName = "the kernel exposes no model operation")]
    public void KernelExposesNoModelOperation()
    {
        var methods = typeof(Neuron)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToArray();

        Assert.DoesNotContain(methods, name => name.Contains("Model", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "no exported DigitalBrain root is an addressable neuron")]
    public void NoExportedDigitalBrainRootIsAnAddressableNeuron()
    {
        var rootName = "Digital" + "Brain";
        var offenders = ProductionAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => string.Equals(type.Name, rootName, StringComparison.Ordinal))
            .Where(type => typeof(Neuron).IsAssignableFrom(type)
                || typeof(INeuron).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "production assemblies expose no retired hosting API")]
    public void ProductionAssembliesExposeNoRetiredHostingApi()
    {
        var exported = ProductionAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .ToArray();
        var rejectedMethods = RejectedHostingMethodNames.ToHashSet(StringComparer.Ordinal);
        var rejectedTypes = RejectedHostingTypeNames.ToHashSet(StringComparer.Ordinal);

        var methodOffenders = exported
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => rejectedMethods.Contains(method.Name))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .ToArray();
        var typeOffenders = exported
            .Where(type => rejectedTypes.Contains(type.Name))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(methodOffenders);
        Assert.Empty(typeOffenders);
    }

    [Fact(DisplayName = "repository source and user documentation contain no retired hosting or composition path")]
    public void RepositoryContainsNoRetiredHostingOrCompositionPath()
    {
        string[] roots = ["src", "modules", "hosts", "samples", "tests", "docs"];
        var guardFile = Path.GetFullPath(Path.Combine(
            RepositoryRoot,
            "tests",
            "DigitalBrain.Tests",
            nameof(ArchitectureCutContracts) + ".cs"));
        var excludedDocs = Path.GetFullPath(Path.Combine(RepositoryRoot, "docs", "superpowers"))
            + Path.DirectorySeparatorChar;

        var violations = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj" or ".md")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !string.Equals(Path.GetFullPath(file), guardFile, StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetFullPath(file).StartsWith(excludedDocs, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => RejectedHostingMethodNames
                .Concat(RejectedHostingTypeNames)
                .Concat(RejectedCompositionSourceIdentifiers)
                .Where(identifier => File.ReadAllText(file).Contains(identifier, StringComparison.Ordinal))
                .Select(identifier => $"{Path.GetRelativePath(RepositoryRoot, file)}: {identifier}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact(DisplayName = "production source contains none of the rejected AI architecture")]
    public void ProductionSourceContainsNoRejectedAiArchitecture()
    {
        string[] roots = ["src", "modules", "hosts", "samples"];

        var violations = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file) is ".cs" or ".csproj")
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(file => RejectedIdentifiers
                .Where(identifier => File.ReadAllText(file).Contains(identifier, StringComparison.Ordinal))
                .Select(identifier => $"{Path.GetRelativePath(RepositoryRoot, file)}: {identifier}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact(DisplayName = "the testing product exports no obsolete L1 surface")]
    public void TestingProductExportsNoObsoleteL1Surface()
    {
        var exported = typeof(DigitalBrainFixture).Assembly
            .GetExportedTypes();
        var typeOffenders = exported
            .Where(type => RejectedTestingTypeNames.Contains(type.Name))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var memberOffenders = exported
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.Static))
            .Where(IsRejectedTestingMember)
            .Select(member =>
                $"{member.DeclaringType?.FullName}.{member.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(typeOffenders);
        Assert.Empty(memberOffenders);
    }

    [Fact(DisplayName = "repository source and durable docs contain no retired hosted-testing surface")]
    public void RepositoryContainsNoRetiredHostedTestingSurface()
    {
        string[] roots = ["src", "tests", "hosts", "docs"];
        var guardFile = Path.GetFullPath(Path.Combine(
            RepositoryRoot,
            "tests",
            "DigitalBrain.Tests",
            nameof(ArchitectureCutContracts) + ".cs"));
        var excludedDocs = Path.GetFullPath(Path.Combine(
            RepositoryRoot,
            "docs",
            "superpowers")) + Path.DirectorySeparatorChar;

        var violations = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Where(file => Path.GetExtension(file)
                is ".cs" or ".csproj" or ".md" or ".mjs")
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !string.Equals(
                Path.GetFullPath(file),
                guardFile,
                StringComparison.OrdinalIgnoreCase))
            .Where(file => !Path.GetFullPath(file).StartsWith(
                excludedDocs,
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => RejectedHostedTestingSourcePatterns
                .Where(pattern => Regex.IsMatch(
                    File.ReadAllText(file),
                    pattern,
                    RegexOptions.CultureInvariant))
                .Select(pattern =>
                    $"{Path.GetRelativePath(RepositoryRoot, file)}: {pattern}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact(DisplayName = "repository source and durable docs contain no obsolete L1 testing vocabulary")]
    public void RepositoryContainsNoObsoleteL1TestingVocabulary()
    {
        string[] sourceRoots = ["src", "modules", "hosts", "samples", "tests"];
        string[] durableDocs =
        [
            "architecture.md",
            "concepts.md",
            "index.md",
            "packages.md",
        ];
        var excludedGuards = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(
                RepositoryRoot,
                "tests",
                "DigitalBrain.Tests",
                nameof(ArchitectureCutContracts) + ".cs")),
            Path.GetFullPath(Path.Combine(
                RepositoryRoot,
                "tests",
                "DigitalBrain.TestingTests",
                "PublicSurfaceContracts.cs")),
            Path.GetFullPath(Path.Combine(
                RepositoryRoot,
                "tests",
                "DigitalBrain.ModuleTests",
                "GherkinArchitecture.cs")),
        };

        var files = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*",
                SearchOption.AllDirectories))
            .Concat(durableDocs.Select(document =>
                Path.Combine(RepositoryRoot, "docs", document)));
        var violations = files
            .Where(file => Path.GetExtension(file)
                is ".cs" or ".csproj" or ".md")
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !excludedGuards.Contains(Path.GetFullPath(file)))
            .SelectMany(file => RejectedTestingSourcePatterns
                .Where(pattern => Regex.IsMatch(
                    File.ReadAllText(file),
                    pattern,
                    RegexOptions.CultureInvariant))
                .Select(pattern =>
                    $"{Path.GetRelativePath(RepositoryRoot, file)}: {pattern}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact(DisplayName = "production projects contain no source-like scratch artifacts")]
    public void ProductionProjectsContainNoSourceLikeScratchArtifacts()
    {
        string[] roots = ["src", "modules", "hosts", "samples"];

        var artifacts = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*.cs.txt",
                SearchOption.AllDirectories))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();

        Assert.Empty(artifacts);
    }

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

    private static IEnumerable<Assembly> ProductionAssemblies()
    {
        foreach (var path in Directory.EnumerateFiles(
            AppContext.BaseDirectory,
            "DigitalBrain*.dll",
            SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains(".Tests", StringComparison.Ordinal)
                || name.Contains(".Simulations", StringComparison.Ordinal)
                || name.Contains(".HostTests", StringComparison.Ordinal))
            {
                continue;
            }

            yield return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        }
    }

    private static bool IsRejectedTestingMember(MemberInfo member)
    {
        if (member.Name == "Grains"
            || member.Name == "Add" + "JsonSerializer"
            || member.Name.StartsWith("Expect", StringComparison.Ordinal)
            || member.Name.StartsWith("Should", StringComparison.Ordinal)
            || member.Name.StartsWith("Match", StringComparison.Ordinal)
            || member.Name.Contains("Matcher", StringComparison.Ordinal)
            || member.Name.StartsWith("Settle", StringComparison.Ordinal)
            || member.Name.Contains("Eventually", StringComparison.Ordinal)
            || member.Name.StartsWith("Delay", StringComparison.Ordinal))
        {
            return true;
        }

        return member is MethodInfo
            {
                Name: "StartAsync" or "StopAsync",
                DeclaringType.Name: var typeName,
            }
            && typeName.Contains("Cluster", StringComparison.Ordinal);
    }
}
