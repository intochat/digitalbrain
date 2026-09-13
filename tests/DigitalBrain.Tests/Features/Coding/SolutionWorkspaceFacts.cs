using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionWorkspaceFacts
{
    private static async Task<SolutionWorkspace> ReadyAsync()
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return workspace;
    }

    [Fact]
    public async Task The_coding_module_composes_into_a_silo()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
        });
        Assert.NotNull(brain.SiloServices.GetService(typeof(SolutionWorkspace)));
    }

    [Fact]
    public void A_fresh_workspace_is_not_opened()
    {
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        Assert.Equal(WorkspacePhase.NotOpened, workspace.Status.Phase);
        Assert.Throws<WorkspaceNotReadyException>(() => workspace.FindSymbolsAsync(new("Greeter"), CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Opening_reports_projects_and_documents()
    {
        using var workspace = await ReadyAsync();
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
        Assert.Equal(2, workspace.Status.ProjectCount);
        Assert.Equal(3, workspace.Status.DocumentCount);
        Assert.Null(workspace.Status.Detail);
    }

    [Fact]
    public async Task Load_failures_stay_visible_on_a_ready_workspace()
    {
        var loader = new FailingAdhocLoader(FixtureSolutions.TwoProjects, ["Alpha.csproj: reference Missing.dll not found"]);
        using var workspace = new SolutionWorkspace(loader, TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
        Assert.Equal("1 load failures; first: Alpha.csproj: reference Missing.dll not found", workspace.Status.Detail);
    }

    private sealed class FailingAdhocLoader(Func<Workspace> open, IReadOnlyList<string> failures) : ISolutionLoader
    {
        public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
            => Task.FromResult(new LoadedSolution(open(), failures));
    }

    [Fact]
    public async Task Find_symbols_returns_the_type_with_a_documentation_id()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalCount);
        var type = Assert.Single(result.Items, hit => hit.Kind == "NamedType");
        Assert.Equal("T:Alpha.Greeter", type.Id);
        Assert.Equal("Alpha", type.Project);
        Assert.Equal(FixtureSolutions.GreeterPath, type.Path);
        Assert.Equal(3, type.Line);
        Assert.Contains(result.Items, hit => hit.Id == "M:Alpha.Greeter.Greet(System.String)");
    }

    [Fact]
    public async Task Find_symbols_honours_the_limit()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.FindSymbolsAsync(new("greet", Limit: 1), TestContext.Current.CancellationToken);
        Assert.Single(result.Items);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task References_cross_the_project_boundary()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.ReferencesAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(result.Items);
        Assert.Equal(FixtureSolutions.ProgramPath, hit.Path);
        Assert.Equal("Beta", hit.Project);
        Assert.Equal(7, hit.Line);
        Assert.Equal("""public static string Run() => new Greeter().Greet("world");""", hit.Text);
    }

    [Fact]
    public async Task References_of_an_unknown_id_is_advice_not_a_crash()
    {
        using var workspace = await ReadyAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.ReferencesAsync(new("T:Nowhere.Missing"), TestContext.Current.CancellationToken));
        Assert.Contains("T:Nowhere.Missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("find-symbols", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_for_a_document_report_its_error()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.DiagnosticsAsync(new(Path: FixtureSolutions.BrokenPath), TestContext.Current.CancellationToken);
        var hit = Assert.Single(result.Items);
        Assert.Equal("CS0029", hit.Id);
        Assert.Equal("Error", hit.Severity);
        Assert.Equal(5, hit.Line);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public async Task Diagnostics_for_a_clean_project_are_empty()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.DiagnosticsAsync(new(Project: "Alpha"), TestContext.Current.CancellationToken);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public async Task Diagnostics_without_a_source_location_are_reported_against_the_project()
    {
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.ConsoleWithoutMain), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var result = await workspace.DiagnosticsAsync(new(Project: "Gamma"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(result.Items);
        Assert.Equal("CS5001", hit.Id);
        Assert.Equal("Error", hit.Severity);
        Assert.Equal("E:/fixture/Gamma/Gamma.csproj", hit.Path);
        Assert.Equal(0, hit.Line);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public async Task The_map_lists_projects_clusters_and_references()
    {
        using var workspace = await ReadyAsync();
        var map = await workspace.MapAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(["Alpha", "Beta"], map.Projects.Select(project => project.Name).Order());
        Assert.Equal("Alpha", Assert.Single(map.Projects, project => project.Name == "Alpha").Cluster);
        Assert.Equal(2, Assert.Single(map.Projects, project => project.Name == "Beta").DocumentCount);
        var edge = Assert.Single(map.References);
        Assert.Equal(("Beta", "Alpha"), (edge.From, edge.To));
    }

    [Fact]
    public async Task Reload_opens_again_and_bumps_nothing_durable()
    {
        var loader = new AdhocSolutionLoader(FixtureSolutions.TwoProjects);
        using var workspace = new SolutionWorkspace(loader, TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        await workspace.BeginReloadAsync();
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, loader.Opens);
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
    }

    [Fact]
    public async Task Dispose_during_a_load_disposes_the_late_workspace()
    {
        var loader = new GatedLoader();
        var workspace = new SolutionWorkspace(loader, TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        var opening = workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        workspace.Dispose();
        var tracking = new DisposalTrackingWorkspace();
        loader.Complete(new LoadedSolution(tracking, []));
        await opening;
        Assert.True(tracking.Disposed);
        Assert.Equal(WorkspacePhase.Failed, workspace.Status.Phase);
        Assert.Equal("the workspace was disposed", workspace.Status.Detail);
    }

    private sealed class GatedLoader : ISolutionLoader
    {
        private readonly TaskCompletionSource<LoadedSolution> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(LoadedSolution loaded) => _source.SetResult(loaded);

        public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
            => _source.Task;
    }

    private sealed class DisposalTrackingWorkspace() : Workspace(MefHostServices.DefaultHost, "Tracking")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool finalize)
        {
            Disposed = true;
            base.Dispose(finalize);
        }
    }

    [Fact]
    public async Task A_loader_failure_becomes_a_failed_status_with_advice()
    {
        using var workspace = new SolutionWorkspace(new ThrowingLoader(), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        Assert.Equal(WorkspacePhase.Failed, workspace.Status.Phase);
        Assert.Equal("disk on fire", workspace.Status.Detail);
        Assert.Contains("failed to open: disk on fire", workspace.Status.Advice, StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<WorkspaceNotReadyException>(() => workspace.WhenReadyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(workspace.Status.Advice, error.Message);
        await Assert.ThrowsAsync<WorkspaceNotReadyException>(() => workspace.FindSymbolsAsync(new("Greeter"), TestContext.Current.CancellationToken));
    }

    private sealed class ThrowingLoader : ISolutionLoader
    {
        public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
            => throw new IOException("disk on fire");
    }
}
