using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DigitalBrain.Architecture.Tests.Unit;

// The framework rings: the core and the UI view model reference nothing.
public sealed class VocabularyRingFacts
{
    // 05-02 (D-15): a capability contract package references the core only.
    // 05-03: secrets@1 and identity@1 follow the same rule.
    [Theory]
    [InlineData("DigitalBrain.Capabilities.Storage")]
    [InlineData("DigitalBrain.Capabilities.Secrets")]
    [InlineData("DigitalBrain.Capabilities.Identity")]
    [InlineData("DigitalBrain.Capabilities.Compute")]
    [InlineData("DigitalBrain.Capabilities.Connections")]
    [InlineData("DigitalBrain.Capabilities.Http")]
    [InlineData("DigitalBrain.Capabilities.Tools")]
    public void Capability_contract_references_only_the_core(string name)
    {
        var project = Repository.ProjectNamed(name).Path;
        string[] expectedProjects = name == "DigitalBrain.Capabilities.Connections"
            ? ["DigitalBrain", "DigitalBrain.Capabilities.Identity", "DigitalBrain.Capabilities.Secrets"]
            : ["DigitalBrain"];
        Assert.Equal(expectedProjects, Repository.ProjectReferences(project).Order(StringComparer.Ordinal));
        string[] expectedPackages = name == "DigitalBrain.Capabilities.Compute"
            ? ["Microsoft.CodeAnalysis.PublicApiAnalyzers", "Microsoft.Extensions.Http"]
            : ["Microsoft.CodeAnalysis.PublicApiAnalyzers"];
        Assert.Equal(expectedPackages, Repository.PackageReferences(project).Order(StringComparer.Ordinal));
    }

    // The host-conformance kit is packable: a consumer runs it against its own host without Azure.
    [Fact]
    public void TestingSiloReferencesNoAzure()
    {
        var project = Repository.ProjectNamed("DigitalBrain.Testing.Silo").Path;
        Assert.Equal(["DigitalBrain.Runtime.Orleans", "DigitalBrain.Testing"], Repository.ProjectReferences(project).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Repository.PackageReferences(project), package => package.Contains("Azure", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "Conformance", "Host", "HostConformanceFacts.cs")));
    }

    [Fact]
    public void View_model_references_no_project()
    {
        Assert.Empty(Repository.ProjectReferences(Repository.ProjectNamed("DigitalBrain.UI").Path));
    }

    // 03-04: the core's only project reference is the analyzer, analyzer-only (no assembly reference).
    [Fact]
    public void Core_references_only_the_analyzer_as_analyzer()
    {
        Assert.Equal(["DigitalBrain.Analyzers"], Repository.ProjectReferences(Repository.ProjectNamed("DigitalBrain").Path));
    }

    [Theory]
    [InlineData("DigitalBrain")]
    [InlineData("DigitalBrain.UI")]
    public void Core_projects_reference_only_the_public_api_analyzer(string name)
    {
        string[] expected = name == "DigitalBrain"
            ? ["Microsoft.CodeAnalysis.PublicApiAnalyzers", "Microsoft.Extensions.Configuration.Binder"]
            : ["Microsoft.CodeAnalysis.PublicApiAnalyzers"];
        Assert.Equal(expected, Repository.PackageReferences(Repository.ProjectNamed(name).Path).Order(StringComparer.Ordinal));
    }
}

// V-02: no Orleans library anywhere in the restore graph of the core-side projects.
public sealed class CoreHasNoOrleansFacts
{
    public static readonly TheoryData<string> CoreProjects = new() { "DigitalBrain", "DigitalBrain.UI", "DigitalBrain.Testing", "DigitalBrain.Analyzers", "DigitalBrain.Capabilities.Storage", "DigitalBrain.Capabilities.Secrets", "DigitalBrain.Capabilities.Identity", "DigitalBrain.Capabilities.Compute", "DigitalBrain.Capabilities.Connections", "DigitalBrain.Capabilities.Http", "DigitalBrain.Capabilities.Tools", "DigitalBrain.Identity.Local", "DigitalBrain.Runtime.Http" };

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void Restore_graph_has_no_orleans(string name)
    {
        var project = Repository.ProjectNamed(name);
        Assert.True(File.Exists(project.Path), project.Path);
        var assets = Path.Combine(project.Directory, "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} is missing; restore {name} first.");

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name).ToList();

        Assert.NotEmpty(libraries);
        Assert.DoesNotContain(libraries, library => library.StartsWith("Microsoft.Orleans", StringComparison.OrdinalIgnoreCase)
            || library.StartsWith("Orleans.", StringComparison.OrdinalIgnoreCase));
    }

    // 04-03 (CORE-01, T-04-08): the built core assemblies reference no Orleans assembly and declare no Orleans namespace.
    public static readonly TheoryData<string> CoreAssemblies = new() { "DigitalBrain", "DigitalBrain.UI", "DigitalBrain.Testing", "DigitalBrain.Capabilities.Storage", "DigitalBrain.Capabilities.Secrets", "DigitalBrain.Capabilities.Identity", "DigitalBrain.Capabilities.Compute", "DigitalBrain.Capabilities.Connections", "DigitalBrain.Capabilities.Http", "DigitalBrain.Capabilities.Tools", "DigitalBrain.Identity.Local", "DigitalBrain.Runtime.Http" };

    private static Assembly Anchor(string name) => name switch
    {
        "DigitalBrain" => typeof(Signal).Assembly,
        "DigitalBrain.UI" => typeof(DigitalBrain.UI.View).Assembly,
        "DigitalBrain.Testing" => typeof(DigitalBrain.Testing.ManualTime).Assembly,
        "DigitalBrain.Capabilities.Storage" => typeof(DigitalBrain.Capabilities.Storage.IStorage).Assembly, // 05-02, D-15
        "DigitalBrain.Capabilities.Secrets" => typeof(DigitalBrain.Capabilities.Secrets.ISecrets).Assembly, // 05-03, D-15
        "DigitalBrain.Capabilities.Identity" => typeof(DigitalBrain.Capabilities.Identity.IIdentity).Assembly, // 05-03, D-15
        "DigitalBrain.Capabilities.Compute" => typeof(DigitalBrain.Capabilities.Compute.ICostMeter).Assembly,
        "DigitalBrain.Capabilities.Connections" => typeof(DigitalBrain.Capabilities.Connections.IConnection).Assembly,
        "DigitalBrain.Capabilities.Http" => typeof(DigitalBrain.Capabilities.Http.IHttpRead).Assembly,
        "DigitalBrain.Capabilities.Tools" => typeof(DigitalBrain.Capabilities.Tools.ITool).Assembly,
        "DigitalBrain.Identity.Local" => typeof(DigitalBrain.Identity.Local.LocalIdentityHosting).Assembly,
        "DigitalBrain.Runtime.Http" => typeof(DigitalBrain.Runtime.Http.IModuleHttpSurface).Assembly, // 09-03, R4-06: ASP.NET, never Orleans
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Assembly_references_no_orleans(string name)
    {
        var assembly = Anchor(name);
        Assert.Equal(name, assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.StartsWith("Orleans", StringComparison.Ordinal) || reference.Name.StartsWith("Microsoft.Orleans", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(CoreAssemblies))]
    public void Assembly_declares_no_orleans_namespace(string name)
    {
        var types = Anchor(name).GetTypes();
        Assert.NotEmpty(types);
        Assert.DoesNotContain(types, type => type.Namespace is { } ns
            && (ns == "Orleans" || ns.StartsWith("Orleans.", StringComparison.Ordinal) || ns.Split('.').Contains("Orleans")));
    }
}

// V-12: the assembly closure of the core reaches no os, Orleans or ASP.NET Core assembly.
public sealed class ReferenceClosureFacts
{
    [Fact]
    public void Core_closure_is_clean()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([typeof(Signal).Assembly, typeof(DigitalBrain.UI.View).Assembly, typeof(DigitalBrain.Capabilities.Storage.IStorage).Assembly, typeof(DigitalBrain.Capabilities.Secrets.ISecrets).Assembly, typeof(DigitalBrain.Capabilities.Identity.IIdentity).Assembly, typeof(DigitalBrain.Capabilities.Compute.ICostMeter).Assembly, typeof(DigitalBrain.Capabilities.Connections.IConnection).Assembly, typeof(DigitalBrain.Capabilities.Http.IHttpRead).Assembly, typeof(DigitalBrain.Capabilities.Tools.ITool).Assembly, typeof(DigitalBrain.Identity.Local.LocalIdentityHosting).Assembly]);
        while (pending.TryPop(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                Assert.False(name.StartsWith("IntoChat.OS", StringComparison.Ordinal)
                    || name.StartsWith("Orleans", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.Orleans", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal), $"{assembly.GetName().Name} references {name}.");
                if (seen.Add(name) && name.StartsWith("DigitalBrain", StringComparison.Ordinal))
                {
                    pending.Push(Assembly.Load(reference));
                }
            }
        }
        Assert.Contains("System.Runtime", seen);
    }
}

// V-07: every packable project is a DigitalBrain package.
public sealed class PackageIdAllowlistFacts
{
    private static readonly string[] Folders = ["src", "templates"];

    private const int Floor = 14; // 05-02: + DigitalBrain.Capabilities.Storage; 05-03: + .Secrets, .Identity; 09-03: + DigitalBrain.Runtime.Http; 0.13: + .Compute, .Connections, .Identity.Local.

    [Fact]
    public void Packable_ids_are_digitalbrain()
    {
        var packable = Folders
            .SelectMany(folder => Repository.SourceFiles(Path.Combine(Repository.Root, folder), "*.csproj"))
            .Select(path => (Path: path, Xml: XDocument.Load(path)))
            .Where(project => project.Xml.Descendants("IsPackable").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
            .Select(project => project.Xml.Descendants("PackageId").FirstOrDefault()?.Value.Trim() ?? Path.GetFileNameWithoutExtension(project.Path))
            .ToList();

        Assert.True(packable.Count >= Floor, $"Expected at least {Floor} packable projects, found {packable.Count}.");
        Assert.All(packable, id => Assert.Matches("^DigitalBrain(\\.|$)", id));
    }
}

// D-14: VERSION is bare.
public sealed class VersionFacts
{
    [Fact]
    public void Version_is_bare_semver()
    {
        Assert.Matches("^\\d+\\.\\d+\\.\\d+$", File.ReadAllText(Path.Combine(Repository.Root, "VERSION")).Trim());
    }
}

// D-01, T-03-10: restore reads nuget.org only.
public sealed class NuGetConfigFacts
{
    [Fact]
    public void Sources_are_remote_only()
    {
        // The nearest nuget.config restore uses: framework/ has none of its own since it folded into the os repo (06-00).
        var directory = new DirectoryInfo(Repository.Root);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "nuget.config"))) { directory = directory.Parent; }
        Assert.NotNull(directory);
        var config = XDocument.Load(Path.Combine(directory.FullName, "nuget.config"));
        var sources = config.Descendants("packageSources").Elements("add").Select(e => e.Attribute("value")!.Value).ToList();

        Assert.NotEmpty(sources);
        Assert.NotEmpty(config.Descendants("packageSources").Elements("clear"));
        Assert.All(sources, value =>
        {
            Assert.StartsWith("https://", value, StringComparison.Ordinal);
            Assert.DoesNotContain("local-feed", value, StringComparison.OrdinalIgnoreCase);
        });
        Assert.NotEmpty(config.Descendants("packageSourceMapping"));
    }
}

// CORE-02: the retired nouns do not come back.
public sealed partial class VocabularyWordFacts
{
    // Connector stays: it is the third type kind beside neuron and signal.
    [GeneratedRegex(@"\b(ITopologyProfile|LocalProfile|LocalCloudProfile|CloudProfile|Provides|Connectors?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Allowed();

    [GeneratedRegex(@"\b(connectors?|providers?|presets?|profiles?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Retired();

    [Fact]
    public void Retired_nouns_are_absent()
    {
        var files = Repository.SourceFiles(Path.Combine(Repository.Root, "src"), "*.cs")
            .Concat(Repository.SourceFiles(Path.Combine(Repository.Root, "src"), "*.md"))
            .Concat(Repository.SourceFiles(Path.Combine(Repository.Root, "docs"), "*.md"))
            .Concat(File.Exists(Path.Combine(Repository.Root, "README.md")) ? [Path.Combine(Repository.Root, "README.md")] : [])
            .ToList();
        Assert.NotEmpty(files);

        var hits = files.SelectMany(file => File.ReadLines(file).Select((line, index) => (file, index, line)))
            .Where(entry => Retired().IsMatch(Allowed().Replace(entry.line, string.Empty)))
            .Select(entry => $"{Path.GetRelativePath(Repository.Root, entry.file)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }
}

// The framework names no product: IntoChat appears in src only in the obsolete shim and the obsolete caller aliases.
public sealed class FrameworkSourceNamesNoProductFacts
{
    private static readonly string[] AllowedFiles = ["IntoChatOSNames.cs", "DigitalBrainConnection.cs", "SessionConnection.cs"];

    [Fact]
    public void FrameworkSourceNamesNoProduct()
    {
        var source = Path.Combine(Repository.Root, "src");
        var files = Repository.SourceFiles(source, "*.cs").ToList();

        var offenders = new List<string>();
        foreach (var path in files.Where(path => !AllowedFiles.Contains(Path.GetFileName(path))))
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (Path.GetFileName(path) == "CapabilityCaller.cs" && (line.Contains("Obsolete", StringComparison.Ordinal) || line.Contains("Assistant", StringComparison.Ordinal))) { continue; }
                if (line.Contains("IntoChat", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(Repository.Root, path)}:{index + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Product names in framework source:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}

public sealed class ScriptFacts
{
    [Fact]
    public void NoScriptFilesExistInRepository()
    {
        var scriptExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sh", ".py", ".ps1", ".pyc"
        };

        var allFiles = Directory.EnumerateFiles(Repository.Root, "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)
                        && !file.EndsWith(Path.DirectorySeparatorChar + ".git", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var offenders = new List<string>();

        foreach (var file in allFiles)
        {
            var relativePath = Path.GetRelativePath(Repository.Root, file).Replace('\\', '/');

            // Check extension
            var ext = Path.GetExtension(file);
            if (scriptExtensions.Contains(ext))
            {
                offenders.Add(relativePath);
                continue;
            }

            // Check scripts/*.cs
            if (relativePath.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) && ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(relativePath);
            }
        }

        Assert.True(offenders.Count == 0, "Script files found in repository:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }
}

