using DigitalBrain.Coding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionFileWatcherFacts
{
    private static async Task<bool> UntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    [Fact]
    public async Task A_saved_source_file_is_folded_into_the_snapshot()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource.Replace("public string Welcome", "public string Hola(string name) => name;\n\n    public string Welcome", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        Assert.True(await UntilAsync(async () => (await workspace.FindSymbolsAsync(new("Hola"), TestContext.Current.CancellationToken)).TotalCount == 1, TimeSpan.FromSeconds(10)));
        Assert.True(workspace.Generation >= 1);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task A_project_file_change_flags_a_reload()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.Root + "/Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);

        Assert.True(await UntilAsync(() => Task.FromResult(workspace.Status.ReloadNeeded), TimeSpan.FromSeconds(10)));
        Assert.Contains("Alpha.csproj", workspace.Status.Detail, StringComparison.Ordinal);
        await workspace.BeginReloadAsync();
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task Files_under_obj_are_ignored_and_a_fold_of_the_same_text_is_a_no_op()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);

        Assert.False(await workspace.FoldAsync(fixture.Root + "/Alpha/obj/Generated.cs", "namespace X;", TestContext.Current.CancellationToken));
        Assert.False(await workspace.FoldAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource, TestContext.Current.CancellationToken));
        Assert.Equal(0, workspace.Generation);
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/Alpha/obj/Debug/x.g.cs"));
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/.git/index"));
        Assert.False(SolutionFileWatcher.IsIgnored(fixture.GreeterPath));
    }
}
