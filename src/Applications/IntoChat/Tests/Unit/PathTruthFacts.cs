using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace IntoChat.Tests.Unit;

public sealed class PathTruthFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string PathInRepo(string relative) => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(PathInRepo(relative));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EveryProjectReferenceResolvesToAnExistingFile()
    {
        var broken = new List<string>();
        foreach (var project in Directory.EnumerateFiles(PathInRepo("src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(project)) { continue; }
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) { continue; }
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                if (!File.Exists(target))
                {
                    broken.Add($"{Path.GetRelativePath(RepositoryRoot, project)} -> {include}");
                }
            }
        }
        Assert.Empty(broken);
    }

    [Fact]
    public void ContainerModuleListsAgreeAndResolve()
    {
        const string prefix = "DigitalBrain__Modules__";
        var docker = Read("src/Applications/IntoChat/IntoChat/Dockerfile").Split('\n')
            .Where(line => line.Contains(prefix, StringComparison.Ordinal))
            .Select(line =>
            {
                var assignment = line.Trim().TrimEnd('\\').Trim();
                if (assignment.StartsWith("ENV ", StringComparison.Ordinal)) { assignment = assignment[4..].Trim(); }
                var match = Regex.Match(assignment, "^DigitalBrain__Modules__(\\d+)=\"([^\"]*)\"$");
                Assert.True(match.Success, $"Malformed module assignment: {line}");
                Assert.NotEmpty(match.Groups[2].Value);
                return (Index: int.Parse(match.Groups[1].Value), Module: match.Groups[2].Value);
            }).ToArray();
        var profile = XDocument.Load(PathInRepo("src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml"))
            .Descendants("ContainerEnvironmentVariable")
            .Where(entry => entry.Attribute("Include")!.Value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => (Index: int.Parse(entry.Attribute("Include")!.Value[prefix.Length..]),
                Module: entry.Attribute("Value")!.Value)).ToArray();

        Assert.NotEmpty(docker);
        Assert.NotEmpty(profile);
        Assert.Equal(docker.Length, docker.Select(entry => entry.Index).Distinct().Count());
        Assert.Equal(profile.Length, profile.Select(entry => entry.Index).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, docker.Length), docker.Select(entry => entry.Index).Order());
        Assert.Equal(Enumerable.Range(0, profile.Length), profile.Select(entry => entry.Index).Order());
        Assert.Equal(docker.Length, docker.Select(entry => entry.Module).Distinct().Count());
        Assert.Equal(profile.Length, profile.Select(entry => entry.Module).Distinct().Count());
        Assert.Equal(docker.Select(entry => entry.Module).Order(StringComparer.Ordinal),
            profile.Select(entry => entry.Module).Order(StringComparer.Ordinal));
        Assert.All(docker.Concat(profile), entry =>
        {
            var type = Type.GetType(entry.Module, throwOnError: true)!;
            Assert.True(typeof(DigitalBrain.Core.IModule).IsAssignableFrom(type));
            Assert.False(DigitalBrain.Contracts.PlatformAssemblyAttribute.IsPlatform(type.Assembly));
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(), reference => reference.Name == "DigitalBrain.Platform");
            Assert.DoesNotContain(type.Namespace, new[] { "DigitalBrain.Microsoft.Aspire", "DigitalBrain.Microsoft.CSharp" });
        });
    }
}
