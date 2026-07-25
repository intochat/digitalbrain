using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests.Packages;

public sealed class ResidualPackageGraphContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName =
        "Client graph is Abstractions + Orleans.Client only — never Kernel, Security, Mcp, or modules")]
    public void ClientGraphIsAbstractionsAndOrleansClientOnly()
    {
        Assert.Equal(
            ["DigitalBrain.Abstractions"],
            DirectCompileProjectReferencesOf("DigitalBrain.Client").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Microsoft.Orleans.Client"],
            DirectPackageReferencesOf("DigitalBrain.Client").Order(StringComparer.Ordinal));

        var reachable = CompileProjectsReachableFrom("DigitalBrain.Client");
        Assert.Equal(["DigitalBrain.Abstractions"], reachable.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            reachable,
            project => project is "DigitalBrain.Kernel" or "DigitalBrain.Security" or "DigitalBrain.Testing"
                || project.StartsWith("DigitalBrain.Integrations.", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "Security is configuration + DI abstractions only — no DigitalBrain projects or provider packages")]
    public void SecurityGraphIsConfigurationAndDiAbstractionsOnly()
    {
        Assert.Empty(DirectCompileProjectReferencesOf("DigitalBrain.Security"));
        Assert.Equal(
            [
                "Microsoft.Extensions.Configuration.Abstractions",
                "Microsoft.Extensions.DependencyInjection.Abstractions",
            ],
            DirectPackageReferencesOf("DigitalBrain.Security").Order(StringComparer.Ordinal));

        Assert.Empty(CompileProjectsReachableFrom("DigitalBrain.Security"));
        Assert.Empty(Assembly.Load("DigitalBrain.Security").GetExportedTypes());
    }

    [Fact(DisplayName =
        "Integrations.Mcp is Security + southbound transport packages only — never Kernel, Client, or modules")]
    public void IntegrationsMcpGraphIsSecurityAndTransportOnly()
    {
        Assert.Equal(
            ["DigitalBrain.Security"],
            DirectCompileProjectReferencesOf("DigitalBrain.Integrations.Mcp").Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "Microsoft.Extensions.Http",
                "Microsoft.Orleans.Journaling",
                "ModelContextProtocol.Core",
            ],
            DirectPackageReferencesOf("DigitalBrain.Integrations.Mcp").Order(StringComparer.Ordinal));

        var reachable = CompileProjectsReachableFrom("DigitalBrain.Integrations.Mcp");
        Assert.Equal(["DigitalBrain.Security"], reachable.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            reachable,
            project => project is "DigitalBrain.Kernel" or "DigitalBrain.Client" or "DigitalBrain.Testing"
                || project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Aspire", StringComparison.Ordinal));

        Assert.DoesNotContain(
            DirectPackageReferencesOf("DigitalBrain.Integrations.Mcp"),
            package => package is "ModelContextProtocol"
                or "ModelContextProtocol.AspNetCore"
                or "Microsoft.AspNetCore.DataProtection"
                || package.StartsWith("OpenAI", StringComparison.Ordinal)
                || package.StartsWith("OllamaSharp", StringComparison.Ordinal)
                || package.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal));

        Assert.Empty(Assembly.Load("DigitalBrain.Integrations.Mcp").GetExportedTypes());
    }

    [Fact(DisplayName =
        "metapackage is Abstractions + Client + Aspire only — never Kernel, Security, Mcp, Testing, or modules")]
    public void MetapackageGraphIsConsumerSurfaceOnly()
    {
        Assert.Equal(
            [
                "DigitalBrain.Abstractions",
                "DigitalBrain.Aspire",
                "DigitalBrain.Client",
            ],
            DirectCompileProjectReferencesOf("DigitalBrain").Order(StringComparer.Ordinal));
        Assert.Empty(DirectPackageReferencesOf("DigitalBrain"));

        var reachable = CompileProjectsReachableFrom("DigitalBrain");
        Assert.Equal(
            [
                "DigitalBrain.Abstractions",
                "DigitalBrain.Aspire",
                "DigitalBrain.Client",
            ],
            reachable.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            reachable,
            project => project is "DigitalBrain.Kernel" or "DigitalBrain.Security" or "DigitalBrain.Testing"
                || project.StartsWith("DigitalBrain.Integrations.", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "Testing graph is Client + Kernel + Integrations.Mcp only — never module runtimes or contracts")]
    public void TestingGraphIsClientKernelAndSouthboundMcpOnly()
    {
        Assert.Equal(
            [
                "DigitalBrain.Client",
                "DigitalBrain.Integrations.Mcp",
                "DigitalBrain.Kernel",
            ],
            DirectCompileProjectReferencesOf("DigitalBrain.Testing").Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "Aspire.Hosting.Testing",
                "Microsoft.Orleans.TestingHost",
                "xunit.v3.extensibility.core",
            ],
            DirectPackageReferencesOf("DigitalBrain.Testing").Order(StringComparer.Ordinal));

        var reachable = CompileProjectsReachableFrom("DigitalBrain.Testing");
        Assert.Equal(
            [
                "DigitalBrain.Abstractions",
                "DigitalBrain.Client",
                "DigitalBrain.Integrations.Mcp",
                "DigitalBrain.Kernel",
                "DigitalBrain.Security",
            ],
            reachable.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            reachable,
            project => project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Aspire", StringComparison.Ordinal)
                || project is "DigitalBrain.Ui"
                || project.StartsWith("DigitalBrain.Ui.", StringComparison.Ordinal));
    }

    private static HashSet<string> CompileProjectsReachableFrom(string package)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([package]);

        while (pending.Count > 0)
        {
            foreach (var reference in DirectCompileProjectReferencesOf(pending.Dequeue()))
            {
                if (reachable.Add(reference))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        return reachable;
    }

    private static IEnumerable<string> DirectCompileProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Where(CompilesAgainst)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    private static IEnumerable<string> DirectPackageReferencesOf(string package) =>
        ReferenceElements(package, "PackageReference")
            .Where(FlowsToConsumers)
            .Select(IncludeOf);

    private static IEnumerable<XElement> ReferenceElements(string package, string elementName) =>
        XDocument.Load(ProjectFileOf(package)).Descendants(elementName);

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)
        && CompilesAgainst(reference);

    private static bool CompilesAgainst(XElement reference) =>
        !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

    private static string IncludeOf(XElement reference) =>
        reference.Attribute("Include")?.Value
        ?? throw new InvalidOperationException($"A {reference.Name.LocalName} element carries no Include attribute.");

    private static string ProjectFileOf(string package) =>
        Directory.EnumerateFiles(RepositoryRoot, $"{package}.csproj", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredLookupPath(file))
            .Single();

    private static bool IsIgnoredLookupPath(string file)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, file);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".worktrees", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
