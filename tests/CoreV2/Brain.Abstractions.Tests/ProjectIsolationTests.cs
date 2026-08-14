using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class ProjectIsolationTests
{
    [Fact]
    public void CoreV2_project_references_do_not_contain_legacy_digitalbrain_projects()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        var projectFiles = Directory.GetFiles(
            Path.Combine(root, "src", "CoreV2"),
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);
        Assert.All(projectFiles, project =>
            Assert.DoesNotContain("DigitalBrain.", File.ReadAllText(project), StringComparison.Ordinal));
    }
}
