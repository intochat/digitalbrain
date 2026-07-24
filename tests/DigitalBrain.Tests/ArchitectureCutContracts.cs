using System.Reflection;
using System.Runtime.Loader;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
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
}
