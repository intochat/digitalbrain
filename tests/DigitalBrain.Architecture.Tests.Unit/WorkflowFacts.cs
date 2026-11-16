using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

public sealed partial class WorkflowFacts
{
    private static string WorkflowsDir => Path.Combine(Repository.Root, ".github", "workflows");

    private static string CiPath => Path.Combine(WorkflowsDir, "ci.yml");

    private static string ReleasePath => Path.Combine(WorkflowsDir, "release.yml");

    [GeneratedRegex(@"^  [a-z0-9-]+:\s*$")]
    private static partial Regex JobHeader();

    [GeneratedRegex(@"^  downstream:\s*$")]
    private static partial Regex DownstreamJob();

    [GeneratedRegex(@"^  template-feed:\s*$")]
    private static partial Regex TemplateFeedJob();

    [Fact]
    public void OnlyCiAndReleaseWorkflowsExist()
    {
        var workflowFiles = Directory.EnumerateFiles(WorkflowsDir, "*.yml")
            .Concat(Directory.EnumerateFiles(WorkflowsDir, "*.yaml"))
            .Select(Path.GetFileName)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["ci.yml", "release.yml"], workflowFiles);
    }

    [Fact]
    public void CiWorkflowGatesGitHubPackagesPublishAwayFromBranchPushAfterCi()
    {
        var content = File.ReadAllText(CiPath);
        if (content.Contains("nuget.pkg.github.com", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("publish-github-packages:", content, StringComparison.Ordinal);
            var publishStart = content.IndexOf("publish-github-packages:", StringComparison.Ordinal);
            var publishSection = content[publishStart..];

            Assert.Contains("needs: [ci]", publishSection, StringComparison.Ordinal);
            Assert.DoesNotContain("refs/heads/master", publishSection, StringComparison.Ordinal);
            Assert.DoesNotContain("refs/heads/main", publishSection, StringComparison.Ordinal);
            Assert.Contains("refs/tags/", publishSection, StringComparison.Ordinal);
            Assert.DoesNotContain("--skip-duplicate", publishSection, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("api.nuget.org", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CiWorkflowContainsNoDownstreamOrTemplateFeedJobs()
    {
        var lines = File.ReadAllLines(CiPath);
        Assert.DoesNotContain(lines, line => DownstreamJob().IsMatch(line));
        Assert.DoesNotContain(lines, line => TemplateFeedJob().IsMatch(line));
    }
}
