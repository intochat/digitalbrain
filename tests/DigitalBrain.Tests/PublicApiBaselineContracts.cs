using Xunit;

namespace DigitalBrain.Tests;

public sealed class PublicApiBaselineContracts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string> PackableProjectNames { get; } = new(PackableProjects.Names);

    [Theory]
    [MemberData(nameof(PackableProjectNames))]
    public void EveryPackableProjectDeclaresItsPublicApiBaseline(string projectName)
    {
        var projectDirectory = Path.Combine(RepositoryRoot, "src", projectName);

        foreach (var baselineFileName in (string[])["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"])
        {
            var baselinePath = Path.Combine(projectDirectory, baselineFileName);
            Assert.True(File.Exists(baselinePath), baselinePath);
            Assert.StartsWith("#nullable enable", File.ReadAllText(baselinePath), StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DigitalBrain.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DigitalBrain.slnx");
    }
}
