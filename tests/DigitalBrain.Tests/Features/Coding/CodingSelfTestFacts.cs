using DigitalBrain.Coding;
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
