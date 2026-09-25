using DigitalBrain.Microsoft.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SolutionWorkspacesFacts
{
    private sealed class NeverLoads : ISolutionLoader
    {
        public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private static SolutionWorkspaces Create(SolutionWorkspace configured)
        => new(configured, new NeverLoads(), NullLogger<SolutionWorkspace>.Instance,
            Options.Create(new RoslynModuleOptions { WorkspaceKey = "digitalbrain" }));

    [Fact]
    public void TheConfiguredKeySharesTheWarmedWorkspaceAndOtherKeysGetTheirOwn()
    {
        using var configured = new SolutionWorkspace(new NeverLoads(), NullLogger<SolutionWorkspace>.Instance);
        using var workspaces = Create(configured);

        Assert.Same(configured, workspaces.For("digitalbrain"));
        Assert.NotSame(configured, workspaces.For("run-a"));
        Assert.NotSame(workspaces.For("run-a"), workspaces.For("run-b"));
        Assert.Same(workspaces.For("run-a"), workspaces.For("run-a"));
    }

    [Fact]
    public void ClosingAKeyGivesItAFreshWorkspaceButNeverClosesTheConfiguredOne()
    {
        using var configured = new SolutionWorkspace(new NeverLoads(), NullLogger<SolutionWorkspace>.Instance);
        using var workspaces = Create(configured);
        var first = workspaces.For("run-a");

        workspaces.Close("run-a");
        workspaces.Close("digitalbrain");

        Assert.NotSame(first, workspaces.For("run-a"));
        Assert.Same(configured, workspaces.For("digitalbrain"));
    }
}
