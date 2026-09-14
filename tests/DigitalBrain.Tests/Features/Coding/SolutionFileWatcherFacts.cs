using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionFileWatcherFacts
{
    [Fact]
    public async Task A_saved_source_file_is_folded_into_the_snapshot()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, TimeProvider.System, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource.Replace("public string Welcome", "public string Hola(string name) => name;\n\n    public string Welcome", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        await TestWait.UntilAsync(async () => (await workspace.FindSymbolsAsync(new("Hola"), TestContext.Current.CancellationToken)).TotalCount,
            count => count == 1, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(workspace.SnapshotVersion >= 1);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task A_project_file_change_flags_a_reload()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        using var watcher = new SolutionFileWatcher(workspace, TimeProvider.System, NullLogger<SolutionFileWatcher>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        watcher.Start(fixture.Root);

        await File.WriteAllTextAsync(fixture.Root + "/Alpha/Alpha.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken);

        var flagged = await TestWait.UntilAsync(() => Task.FromResult(workspace.Status), status => status.ReloadNeeded,
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Contains("Alpha.csproj", flagged.Detail, StringComparison.Ordinal);
        await workspace.BeginReloadAsync();
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.False(workspace.Status.ReloadNeeded);
    }

    [Fact]
    public async Task A_fold_of_an_unknown_path_or_unchanged_text_is_a_no_op()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);

        Assert.False(await workspace.FoldAsync(fixture.Root + "/Alpha/obj/Generated.cs", "namespace X;", TestContext.Current.CancellationToken));
        Assert.False(await workspace.FoldAsync(fixture.GreeterPath, FixtureSolutions.GreeterSource, TestContext.Current.CancellationToken));
        Assert.Equal(0, workspace.SnapshotVersion);
    }

    [Fact]
    public void Build_and_version_control_folders_are_ignored()
    {
        using var fixture = DiskFixture.Create();
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/Alpha/obj/Debug/x.g.cs"));
        Assert.True(SolutionFileWatcher.IsIgnored(fixture.Root + "/.git/index"));
        Assert.False(SolutionFileWatcher.IsIgnored(fixture.GreeterPath));
    }
}
