using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

public sealed partial class WorkflowFacts
{
    private static string CiPath => Path.Combine(Repository.Root, ".github", "workflows", "ci.yml");

    private static string ReleasePath => Path.Combine(Repository.Root, ".github", "workflows", "release.yml");

    [GeneratedRegex(@"^  [a-z0-9-]+:\s*$")]
    private static partial Regex JobHeader();

    [GeneratedRegex(@"^  downstream:\s*$")]
    private static partial Regex DownstreamJob();

    private static string JobBlock(string workflowPath, string jobName)
    {
        var lines = File.ReadAllLines(workflowPath);
        var jobsIndex = Array.FindIndex(lines, line => line.TrimEnd() == "jobs:");
        Assert.True(jobsIndex >= 0, $"'jobs:' section not found in {workflowPath}.");

        var header = $"  {jobName}:";
        var start = Array.FindIndex(lines, jobsIndex + 1, line => line.TrimEnd() == header);
        Assert.True(start >= 0, $"Job '{jobName}' not found in {workflowPath}.");

        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (JobHeader().IsMatch(lines[i]))
            {
                end = i;
                break;
            }
        }

        return string.Join('\n', lines[start..end]);
    }

    [Fact]
    public void Publish_pushes_the_preview_to_nuget_org_and_github_packages()
    {
        var publish = JobBlock(CiPath, "publish");
        Assert.Contains("api.nuget.org/v3/index.json", publish, StringComparison.Ordinal);
        Assert.Contains("nuget.pkg.github.com/intochat/index.json", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_and_release_dispatch_to_both_consumers()
    {
        foreach (var (name, block) in new[] { ("ci.yml#publish", JobBlock(CiPath, "publish")), ("release.yml#release", JobBlock(ReleasePath, "release")) })
        {
            Assert.True(block.Contains("gh api repos/intochat/modules/dispatches", StringComparison.Ordinal), $"{name} does not dispatch to intochat/modules.");
            Assert.True(block.Contains("gh api repos/intochat/os/dispatches", StringComparison.Ordinal), $"{name} does not dispatch to intochat/os.");
            Assert.True(block.Contains("event_type=digitalbrain-published", StringComparison.Ordinal), $"{name} does not send event_type=digitalbrain-published.");
            Assert.True(block.Contains("client_payload[version]", StringComparison.Ordinal), $"{name} does not send client_payload[version].");
        }
    }

    [Fact]
    public void No_downstream_job_remains()
    {
        var lines = File.ReadAllLines(CiPath);
        Assert.DoesNotContain(lines, line => DownstreamJob().IsMatch(line));
    }
}
