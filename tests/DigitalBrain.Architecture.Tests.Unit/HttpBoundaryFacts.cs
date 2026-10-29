using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DigitalBrain.Architecture.Tests.Unit;

// 09-03 (Phase 5 D-15, 09-CONTEXT R4-06): ASP.NET Core lives in DigitalBrain.Runtime.Http only. The core, the UI view model,
// Testing and the capability contracts stay free of ASP.NET Core (and of Orleans, CoreHasNoOrleansFacts); Runtime.Http itself
// is Orleans-free and references the ASP.NET Core shared framework plus the identity contract only.
public sealed partial class HttpBoundaryFacts
{
    private const string HttpProject = "DigitalBrain.Runtime.Http";

    public static readonly TheoryData<string> AspNetFreeProjects = new()
    {
        "DigitalBrain", "DigitalBrain.UI", "DigitalBrain.Testing", "DigitalBrain.Analyzers",
        "DigitalBrain.Capabilities.Storage", "DigitalBrain.Capabilities.Secrets", "DigitalBrain.Capabilities.Identity",
    };

    [GeneratedRegex(@"^\s*using\s+(static\s+)?Microsoft\.AspNetCore(\.|;)", RegexOptions.Multiline)]
    private static partial Regex AspNetUse();

    [Theory]
    [MemberData(nameof(AspNetFreeProjects))]
    public void Project_has_no_aspnet_framework_or_library(string name)
    {
        var project = Repository.ProjectNamed(name);
        Assert.DoesNotContain(FrameworkReferences(project.Path), reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        var assets = Path.Combine(project.Directory, "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} is missing; restore {name} first.");
        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name).ToList();
        Assert.DoesNotContain(libraries, library => library.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Microsoft.AspNetCore.App", AssetFrameworkReferences(document));
    }

    [Fact]
    public void Built_contract_core_and_testing_assemblies_reference_no_aspnet()
    {
        Assembly[] assemblies =
        [
            typeof(Signal).Assembly, typeof(DigitalBrain.UI.View).Assembly, typeof(DigitalBrain.Testing.ManualTime).Assembly,
            typeof(DigitalBrain.Capabilities.Storage.IStorage).Assembly, typeof(DigitalBrain.Capabilities.Secrets.ISecrets).Assembly,
            typeof(DigitalBrain.Capabilities.Identity.IIdentity).Assembly,
        ];
        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Aspnet_source_appears_only_in_runtime_http()
    {
        var files = Repository.SourceFiles(Path.Combine(Repository.Root, "src"), "*.cs").ToList();
        var http = Path.Combine(Repository.Root, "src", HttpProject) + Path.DirectorySeparatorChar;
        Assert.Contains(files, file => file.StartsWith(http, StringComparison.Ordinal) && AspNetUse().IsMatch(File.ReadAllText(file)));
        var hits = files.Where(file => !file.StartsWith(http, StringComparison.Ordinal) && AspNetUse().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(Repository.Root, file)).ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void Runtime_http_references_the_aspnet_framework_and_identity_contract_only()
    {
        var project = Repository.ProjectNamed(HttpProject);
        Assert.Equal(["DigitalBrain.Capabilities.Identity"], Repository.ProjectReferences(project.Path));
        Assert.Equal(["Microsoft.CodeAnalysis.PublicApiAnalyzers"], Repository.PackageReferences(project.Path));
        Assert.Equal(["Microsoft.AspNetCore.App"], FrameworkReferences(project.Path));
        var restored = Path.Combine(project.Directory, "obj", "project.assets.json");
        Assert.True(File.Exists(restored), $"{restored} is missing; restore {HttpProject} first.");
        using (var restoredDocument = JsonDocument.Parse(File.ReadAllText(restored))) { Assert.Contains("Microsoft.AspNetCore.App", AssetFrameworkReferences(restoredDocument)); }
        var assets = Path.Combine(project.Directory, "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} is missing; restore {HttpProject} first.");
        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name).ToList();
        Assert.DoesNotContain(libraries, library => library.StartsWith("Microsoft.Orleans", StringComparison.OrdinalIgnoreCase)
            || library.StartsWith("Orleans.", StringComparison.OrdinalIgnoreCase) || library.StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(DigitalBrain.Runtime.Http.IModuleHttpSurface).Assembly.GetReferencedAssemblies(),
            reference => reference.Name!.StartsWith("Orleans", StringComparison.Ordinal) || reference.Name.StartsWith("Microsoft.Orleans", StringComparison.Ordinal));
    }

    private static List<string> FrameworkReferences(string csproj)
        => [.. XDocument.Load(csproj).Descendants("FrameworkReference").Select(reference => reference.Attribute("Include")?.Value).OfType<string>()];

    private static List<string> AssetFrameworkReferences(JsonDocument assets)
    {
        var names = new List<string>();
        if (!assets.RootElement.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks)) { return names; }
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.TryGetProperty("frameworkReferences", out var references))
            {
                names.AddRange(references.EnumerateObject().Select(reference => reference.Name));
            }
        }

        return names;
    }
}
