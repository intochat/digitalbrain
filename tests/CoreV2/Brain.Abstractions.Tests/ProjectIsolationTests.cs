using System.Xml.Linq;
using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class ProjectIsolationTests
{
    [Fact]
    public void CoreV2_project_references_resolve_inside_the_CoreV2_source_root()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        var coreV2Root = Path.Combine(root, "src", "CoreV2");
        var projectFiles = Directory.GetFiles(
            coreV2Root,
            "*.csproj",
            SearchOption.AllDirectories);

        Assert.NotEmpty(projectFiles);
        Assert.All(projectFiles, project =>
        {
            var projectDirectory = Path.GetDirectoryName(project)!;
            var references = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(static element => (string?)element.Attribute("Include"))
                .Where(static include => include is not null);

            Assert.All(references, reference =>
            {
                var resolvedReference = Path.GetFullPath(Path.Combine(projectDirectory, reference!));
                Assert.StartsWith(
                    coreV2Root + Path.DirectorySeparatorChar,
                    resolvedReference,
                    StringComparison.OrdinalIgnoreCase);
            });
        });
    }
}
