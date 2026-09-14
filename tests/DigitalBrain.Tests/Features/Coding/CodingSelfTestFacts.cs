using DigitalBrain.Coding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// Opens this repository's own solution through MSBuild. Slow (about a minute) and needs a restored tree,
// so it runs only when DIGITALBRAIN_CODING_SELF_TESTS=1.
public sealed class CodingSelfTestFacts
{
    private const string Skip = "Set DIGITALBRAIN_CODING_SELF_TESTS=1 to open the real solution through MSBuild (about a minute, restored tree required).";

    private static readonly string SolutionPath = FindSolution();

    public static bool SelfTestsEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_CODING_SELF_TESTS") == "1";

    [Fact(Skip = Skip, SkipUnless = nameof(SelfTestsEnabled))]
    public async Task The_real_solution_opens_and_answers_a_known_reference()
    {
        using var workspace = new SolutionWorkspace(new MSBuildSolutionLoader(), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(SolutionPath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await workspace.WhenReadyAsync(timeout.Token);
        Assert.True(workspace.Status.ProjectCount >= 30, workspace.Status.Detail);
        // A partial load must never pass as ready on our own solution (design section 8, finding 5).
        Assert.Null(workspace.Status.Detail);

        var symbols = await workspace.FindSymbolsAsync(new("ITimer"), timeout.Token);
        var contract = Assert.Single(symbols.Items, hit => hit.Id == "T:DigitalBrain.Time.ITimer");
        var references = await workspace.ReferencesAsync(new(contract.Id), timeout.Token);
        Assert.Contains(references.Items, hit => hit.Path.EndsWith("TimerNeuron.cs", StringComparison.OrdinalIgnoreCase));

        var map = await workspace.MapAsync(new(), timeout.Token);
        Assert.Contains(map.Projects, project => project.Name == "DigitalBrain.Modules.Time" && project.Cluster == "Modules/Time");
        Assert.Contains(map.References, edge => edge.From == "DigitalBrain.Modules.Time" && edge.To == "DigitalBrain");
    }

    [Fact(Skip = Skip, SkipUnless = nameof(SelfTestsEnabled))]
    public async Task The_real_solution_builds_and_one_class_tests_through_the_runner()
    {
        var artifacts = Path.Combine(Path.GetDirectoryName(SolutionPath)!, "artifacts", "self-test");
        var runner = new DotnetRunner(new ProcessRunner());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        var build = await runner.BuildAsync(SolutionPath, artifacts, timeout.Token);
        Assert.True(build.Succeeded, build.Detail ?? string.Join("; ", build.Errors.Select(error => $"{error.Path}:{error.Line} {error.Id}")));
        Assert.Empty(build.Errors);

        var tests = await runner.TestAsync(Path.Combine(Path.GetDirectoryName(SolutionPath)!, "tests", "DigitalBrain.Tests", "DigitalBrain.Tests.csproj"),
            "DigitalBrain.Tests.Coding.WorkspaceReadFacts", artifacts, timeout.Token);
        Assert.True(tests.Succeeded, tests.Detail ?? string.Join("; ", tests.Failures.Select(failure => failure.Name)));
        Assert.True(tests.Passed >= 7, $"passed {tests.Passed}");
    }

    [Fact(Skip = Skip, SkipUnless = nameof(SelfTestsEnabled))]
    public async Task The_real_solution_builds_into_the_standby_slot_output()
    {
        var repository = Path.GetDirectoryName(SolutionPath)!;
        var options = SlotOptions.From(new ConfigurationBuilder().Build());
        var artifacts = options.ArtifactsFor("b", repository);
        Assert.True(Path.IsPathFullyQualified(artifacts), artifacts);
        Assert.Equal(Path.Combine(repository, "artifacts", "slot-b"), artifacts);

        var runner = new DotnetRunner(new ProcessRunner());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var build = await runner.BuildAsync(SolutionPath, artifacts, timeout.Token);

        Assert.True(build.Succeeded, build.Detail ?? string.Join("; ", build.Errors.Select(error => $"{error.Path}:{error.Line} {error.Id}")));
        // The AppHost starts kernel-b from exactly this path (spike S1).
        Assert.True(File.Exists(Path.Combine(artifacts, "bin", "DigitalBrain.Silo", "release", "DigitalBrain.Silo.dll")),
            $"the standby dll is missing under {artifacts}");
    }

    private static string FindSolution()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null ? "DigitalBrain.slnx" : Path.Combine(directory.FullName, "DigitalBrain.slnx");
    }
}
