using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

public sealed partial class TestTierFacts
{
    private static readonly HashSet<string> AllowedTierSuffixes = new(StringComparer.Ordinal)
    {
        ".Tests.Unit",
        ".Tests.Integration",
        ".Tests.E2E",
    };

    private static readonly HashSet<string> RuntimeUnitExceptions = new(StringComparer.Ordinal)
    {
        "DigitalBrain.Runtime.Orleans.Tests.Unit",
        "DigitalBrain.Runtime.Http.Tests.Unit",
    };

    [GeneratedRegex(@"\[(Fact|Theory)\b")]
    private static partial Regex FactOrTheory();

    [Fact]
    public void EveryTestProjectIsInDigitalBrainSlnxAndFollowsTierNaming()
    {
        var slnxProjects = Repository.Projects.Values
            .Where(p => p.Name.Contains(".Tests.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(slnxProjects);
        foreach (var project in slnxProjects)
        {
            Assert.True(
                AllowedTierSuffixes.Any(suffix => project.Name.EndsWith(suffix, StringComparison.Ordinal)),
                $"{project.Name} does not end with .Tests.Unit, .Tests.Integration, or .Tests.E2E.");
        }

        var onDiskTestProjects = Repository.SourceFiles(Path.Combine(Repository.Root, "tests"), "*.csproj")
            .Concat(Repository.SourceFiles(Path.Combine(Repository.Root, "examples"), "*.csproj")
                .Where(p => Path.GetFileNameWithoutExtension(p).Contains(".Tests.", StringComparison.Ordinal)))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToList();

        foreach (var name in onDiskTestProjects)
        {
            Assert.True(Repository.Projects.ContainsKey(name), $"{name} is on disk but missing from {Repository.SolutionFile}.");
        }
    }

    [Fact]
    public void UnitTierReferencesNoRuntimeExceptOwnRuntimeUnitTests()
    {
        var unitProjects = Repository.Projects.Values
            .Where(p => p.Name.EndsWith(".Tests.Unit", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(unitProjects);
        foreach (var project in unitProjects)
        {
            if (RuntimeUnitExceptions.Contains(project.Name))
            {
                continue;
            }

            var refs = Repository.ProjectReferences(project.Path)
                .Concat(Repository.PackageReferences(project.Path))
                .ToList();

            Assert.DoesNotContain(refs, r => r.StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal));
            Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.Orleans", StringComparison.Ordinal));
            Assert.DoesNotContain(refs, r => r.StartsWith("Orleans.", StringComparison.Ordinal));
            Assert.DoesNotContain(refs, r => r.Equals("DigitalBrain.Testing.Silo", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IntegrationTierUsesInProcessRuntimeOrTemplateProcessWithoutAspireOrPlaywright()
    {
        var integrationProjects = Repository.Projects.Values
            .Where(p => p.Name.EndsWith(".Tests.Integration", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(integrationProjects);
        foreach (var project in integrationProjects)
        {
            var refs = Repository.ProjectReferences(project.Path)
                .Concat(Repository.PackageReferences(project.Path))
                .ToList();

            Assert.DoesNotContain(refs, r => r.Contains("Aspire.Hosting", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(refs, r => r.Contains("Playwright", StringComparison.OrdinalIgnoreCase));

            if (project.Name != "DigitalBrain.Templates.Tests.Integration"
                && project.Name != "DigitalBrain.Runtime.Http.Tests.Integration")
            {
                Assert.Contains(refs, r =>
                    r == "DigitalBrain.Testing.Silo"
                    || r.StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void EndToEndTierIsOnlyFrameworkHostConformance()
    {
        var e2eProjects = Repository.Projects.Values
            .Where(p => p.Name.EndsWith(".Tests.E2E", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(["DigitalBrain.Framework.Tests.E2E"], e2eProjects);
    }

    [Fact]
    public void SimulationShipsNoTestFramework()
    {
        var simulation = Repository.ProjectNamed("DigitalBrain.Simulation").Path;
        Assert.DoesNotContain(Repository.PackageReferences(simulation), package => package.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Repository.ProjectReferences(simulation), reference => reference.StartsWith("DigitalBrain.Testing", StringComparison.Ordinal));
        Assert.DoesNotContain(Repository.ProjectReferences(simulation), reference => reference.StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal));
    }

    [Fact]
    public void NoTestProjectIsEmpty()
    {
        var testProjects = Repository.Projects.Values
            .Where(p => p.Name.Contains(".Tests.", StringComparison.Ordinal))
            .ToList();

        foreach (var project in testProjects)
        {
            var hasFact = Repository.SourceFiles(project.Directory, "*.cs")
                .Any(cs => FactOrTheory().IsMatch(File.ReadAllText(cs)));
            Assert.True(hasFact, $"{project.Name} has no [Fact] or [Theory] methods.");
        }
    }
}
