using System.Text.Json;
using System.Xml.Linq;

namespace DigitalBrain.Architecture.Tests;

// The test-tier architecture: a module proves its mechanism in one in-process test project on
// DigitalBrain.Testing.Module; the product proves the composition through the E2E tier on
// DigitalBrain.Testing.E2E; a full product stack boots in a shared fixture, never per fact.
// TestTierBaseline.json records the not-yet-migrated legacy state. Every list only shrinks:
// a new entry authorizes an architecture regression and needs the same deliberate review as
// the persisted-state baseline.
public sealed class TestTierFacts
{
    [Fact]
    public void AModuleHasOneTestProjectNamedTests()
    {
        var baseline = Baseline().LegacyModuleTestProjects.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        var conformingPerDirectory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in ModuleTestProjects())
        {
            var name = Path.GetFileNameWithoutExtension(project);
            if (name.Contains(".Tests.", StringComparison.Ordinal))
            {
                if (!baseline.Remove(name)) { offenders.Add(name); }
                continue;
            }
            var directory = Path.GetDirectoryName(Path.GetDirectoryName(project))!;
            conformingPerDirectory[directory] = conformingPerDirectory.GetValueOrDefault(directory) + 1;
        }
        Assert.True(offenders.Count == 0,
            "A module ships one test project named <Module>.Tests. Migrate instead of adding: " + string.Join(", ", offenders));
        Assert.True(baseline.Count == 0,
            "Migrated projects linger in TestTierBaseline.json LegacyModuleTestProjects; remove: " + string.Join(", ", baseline));
        var crowded = conformingPerDirectory.Where(entry => entry.Value > 1).Select(entry => entry.Key).ToArray();
        Assert.True(crowded.Length == 0, "One test project per module tests folder: " + string.Join(", ", crowded));
    }

    [Fact]
    public void ModuleTestsStayInProcess()
    {
        var baseline = Baseline().LegacyAspireReferencingModuleTests.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        foreach (var project in ModuleTestProjects())
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var hosting = References(project)
                .Where(reference => reference is "DigitalBrain.Aspire.Server" || reference.EndsWith(".Aspire.Hosting", StringComparison.Ordinal))
                .ToArray();
            if (hosting.Length == 0) { continue; }
            if (!baseline.Remove(name)) { offenders.Add($"{name} -> {string.Join(", ", hosting)}"); }
        }
        Assert.True(offenders.Count == 0,
            "Module tests host the brain in-process (DigitalBrain.Testing.Module); Aspire hosting belongs to the product tier: "
            + string.Join("; ", offenders));
        Assert.True(baseline.Count == 0,
            "Migrated projects linger in TestTierBaseline.json LegacyAspireReferencingModuleTests; remove: " + string.Join(", ", baseline));
    }

    [Fact]
    public void TheEndToEndTierBelongsToTheProduct()
    {
        string[] productScope = ["DigitalBrain.Kernel.Tests.E2E", "DigitalBrain.Platform.Tests.E2E", "DigitalBrain.Aspire.Hosting.Tests.E2E", "DigitalBrain.OS.Tests.E2E"];
        var baseline = Baseline().LegacyEndToEndReferencers.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        foreach (var project in TestProjects())
        {
            if (!References(project).Contains("DigitalBrain.Testing.E2E", StringComparer.Ordinal)) { continue; }
            var name = Path.GetFileNameWithoutExtension(project);
            var relative = Relative(project);
            if (relative.StartsWith("src/IntoChat/", StringComparison.Ordinal)
                || relative.StartsWith("src/Testing/", StringComparison.Ordinal)
                || productScope.Contains(name, StringComparer.Ordinal))
            { continue; }
            if (!baseline.Remove(name)) { offenders.Add(name); }
        }
        Assert.True(offenders.Count == 0,
            "DigitalBrain.Testing.E2E is the product's tier (IntoChat, Kernel, Platform, Aspire hosting). A module's edge belongs in its in-process .Tests project: "
            + string.Join(", ", offenders));
        Assert.True(baseline.Count == 0,
            "Migrated projects linger in TestTierBaseline.json LegacyEndToEndReferencers; remove: " + string.Join(", ", baseline));
    }

    [Fact]
    public void FullProductStacksBootInFixturesNotInFacts()
    {
        // Concatenated so this file never matches its own rule.
        string[] boots =
        [
            "ReferenceBrain" + ".Create(",
            "E2ETest" + ".Create(",
            "E2ETest" + ".For<",
            "IntoChatE2ETest" + ".Create(",
            "DistributedApplication" + "TestingBuilder",
        ];
        var baseline = Baseline().LegacyFullStackBootFacts.ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src"), "*Facts.cs", SearchOption.AllDirectories))
        {
            if (file.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")) { continue; }
            var source = File.ReadAllText(file);
            if (!boots.Any(token => source.Contains(token, StringComparison.Ordinal))) { continue; }
            var relative = Relative(file);
            if (!baseline.Remove(relative)) { offenders.Add(relative); }
        }
        Assert.True(offenders.Count == 0,
            "A full product stack boots once per suite, in a shared fixture; a fact leases it instead of booting its own: "
            + string.Join(", ", offenders));
        Assert.True(baseline.Count == 0,
            "Migrated facts linger in TestTierBaseline.json LegacyFullStackBootFacts; remove: " + string.Join(", ", baseline));
    }

    private sealed record TestTierBaseline(
        string[] LegacyModuleTestProjects,
        string[] LegacyEndToEndReferencers,
        string[] LegacyAspireReferencingModuleTests,
        string[] LegacyFullStackBootFacts);

    private static TestTierBaseline Baseline()
        => JsonSerializer.Deserialize<TestTierBaseline>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestTierBaseline.json")))!;

    private static readonly string Root = RepositoryRoot();

    private static IEnumerable<string> TestProjects()
        => Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => path.Split(Path.DirectorySeparatorChar)
                .Any(part => string.Equals(part, "tests", StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> ModuleTestProjects()
        => TestProjects().Where(path => Relative(path).StartsWith("src/Modules/", StringComparison.Ordinal));

    private static IEnumerable<string> References(string project)
        => XDocument.Load(project).Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Where(include => !include.Contains('@', StringComparison.Ordinal))
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')));

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DigitalBrain.slnx was not found.");
    }
}
