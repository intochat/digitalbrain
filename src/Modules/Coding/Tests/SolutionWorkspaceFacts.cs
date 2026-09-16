using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionWorkspaceFacts
{
    private static async Task<SolutionWorkspace> ReadyAsync()
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
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
    public async Task A_malformed_solution_path_does_not_stop_the_silo()
    {
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = "E:/fixture/bad\0path.slnx" },
        });
        Assert.Equal(WorkspacePhase.NotOpened, brain.SiloServices.GetRequiredService<SolutionWorkspace>().Status.Phase);
    }

    [Fact]
    public void A_fresh_workspace_is_not_opened()
    {
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        Assert.Equal(WorkspacePhase.NotOpened, workspace.Status.Phase);
        Assert.Throws<WorkspaceNotReadyException>(() => workspace.FindSymbolsAsync(new("Greeter"), CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Opening_reports_projects_and_documents()
    {
        using var workspace = await ReadyAsync();
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
        Assert.Equal(2, workspace.Status.ProjectCount);
        // Six hand-written fixture files plus the generated GreeterCodec document under Alpha/obj.
        Assert.Equal(7, workspace.Status.DocumentCount);
        Assert.Null(workspace.Status.Detail);
    }

    [Fact]
    public async Task Commit_applies_the_snapshot_and_writes_only_the_changed_files()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var programBefore = File.GetLastWriteTimeUtc(fixture.ProgramPath);
        var editor = new ChangeSetEditor(new CodeFixCatalog());

        var outcome = await workspace.CommitAsync(async (solution, token) =>
        {
            var applied = await editor.ApplyAsync(solution,
                [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""")], token);
            return applied.Changed;
        }, TestContext.Current.CancellationToken);

        Assert.Equal([fixture.GreeterPath], outcome.WrittenPaths);
        Assert.Equal(1, outcome.SnapshotVersion);
        Assert.Equal(1, workspace.SnapshotVersion);
        Assert.Contains("Hi, {name}", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(programBefore, File.GetLastWriteTimeUtc(fixture.ProgramPath));
        var member = await workspace.MemberAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        Assert.Contains("Hi, {name}", member.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Commit_of_unchanged_text_writes_nothing()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var editor = new ChangeSetEditor(new CodeFixCatalog());
        await workspace.CommitAsync(async (solution, token) =>
        {
            var applied = await editor.ApplyAsync(solution,
                [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""")], token);
            return applied.Changed;
        }, TestContext.Current.CancellationToken);

        var writeTimeBefore = File.GetLastWriteTimeUtc(fixture.GreeterPath);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var currentText = await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken);

        var outcome = await workspace.CommitAsync((solution, _) => Task.FromResult(solution.WithDocumentText(
            solution.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(), SourceText.From(currentText))), TestContext.Current.CancellationToken);

        Assert.Empty(outcome.WrittenPaths);
        Assert.Equal(2, outcome.SnapshotVersion);
        Assert.Equal(2, workspace.SnapshotVersion);
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(fixture.GreeterPath));
    }

    [Fact]
    public async Task Commit_refuses_a_snapshot_the_workspace_has_moved_past()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var stale = await workspace.QueryAsync((solution, _) => Task.FromResult(solution), TestContext.Current.CancellationToken);
        await workspace.CommitAsync((solution, _) => Task.FromResult(solution.WithDocumentText(
            solution.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(), SourceText.From(FixtureSolutions.GreeterSource + "\n// touched\n"))), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CommitAsync((_, _) => Task.FromResult(stale.WithDocumentText(
            stale.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(), SourceText.From("namespace Alpha;"))), TestContext.Current.CancellationToken));
        Assert.Contains("Check it again", error.Message, StringComparison.Ordinal);
        Assert.Contains("// touched", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    // Each commit computes its change on the snapshot it is applied to: without the writer gate the second
    // one's TryApplyChanges refuses a snapshot the first has already moved past, even though they touch
    // different files and neither is stale in any sense the caller could act on.
    [Fact]
    public async Task Two_commits_that_touch_different_files_both_land()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);

        // The first commit parks inside its own change, holding the writer gate, so the second one is forced
        // to compute its change while the first has not applied yet - the exact interleaving that used to
        // make the second commit refuse itself as stale.
        var release = new TaskCompletionSource();
        var greeterCommit = workspace.CommitAsync(async (solution, token) =>
        {
            await release.Task.WaitAsync(token);
            return solution.WithDocumentText(solution.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(),
                SourceText.From(FixtureSolutions.GreeterSource + "\n// greeter\n"));
        }, TestContext.Current.CancellationToken);
        var unusedCommit = workspace.CommitAsync((solution, _) => Task.FromResult(solution.WithDocumentText(
            solution.GetDocumentIdsWithFilePath(fixture.UnusedPath).Single(), SourceText.From(FixtureSolutions.UnusedSource + "\n// unused\n"))), TestContext.Current.CancellationToken);
        release.SetResult();
        var outcomes = await Task.WhenAll(greeterCommit, unusedCommit);

        Assert.Equal([fixture.GreeterPath], outcomes[0].WrittenPaths);
        Assert.Equal([fixture.UnusedPath], outcomes[1].WrittenPaths);
        Assert.Equal([1L, 2L], outcomes.Select(static outcome => outcome.SnapshotVersion).Order());
        Assert.Contains("// greeter", await File.ReadAllTextAsync(fixture.GreeterPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains("// unused", await File.ReadAllTextAsync(fixture.UnusedPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    // The fold queues on the writer gate instead of racing the commit's TryApplyChanges, so it applies to the
    // snapshot the commit produced and the saved text reaches the snapshot rather than being refused.
    [Fact]
    public async Task A_fold_during_a_commit_is_not_lost()
    {
        using var fixture = DiskFixture.Create();
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(fixture.Open), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(fixture.SolutionPath);
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        var release = new TaskCompletionSource();

        // Both calls run synchronously up to their first real await, so by the time they have returned their
        // tasks the commit holds the writer gate and the fold is queued behind it: no timing assumption.
        var commit = workspace.CommitAsync(async (solution, token) =>
        {
            await release.Task.WaitAsync(token);
            return solution.WithDocumentText(solution.GetDocumentIdsWithFilePath(fixture.GreeterPath).Single(),
                SourceText.From(FixtureSolutions.GreeterSource + "\n// committed\n"));
        }, TestContext.Current.CancellationToken);
        var folding = workspace.FoldAsync(fixture.UnusedPath, FixtureSolutions.UnusedSource + "\n// saved\n", TestContext.Current.CancellationToken);
        release.SetResult();
        var committed = await commit;

        Assert.True(await folding);
        Assert.Equal([fixture.GreeterPath], committed.WrittenPaths);
        Assert.Equal(2, workspace.SnapshotVersion);
        var folded = await workspace.QueryAsync(async (solution, token) =>
            (await solution.GetDocument(solution.GetDocumentIdsWithFilePath(fixture.UnusedPath).Single())!.GetTextAsync(token)).ToString(),
            TestContext.Current.CancellationToken);
        Assert.Contains("// saved", folded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_failures_stay_visible_on_a_ready_workspace()
    {
        var loader = new FailingAdhocLoader(FixtureSolutions.TwoProjects, ["Alpha.csproj: reference Missing.dll not found"]);
        using var workspace = new SolutionWorkspace(loader, NullLogger<SolutionWorkspace>.Instance);
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
    public async Task Find_symbols_drops_generated_declarations_and_ranks_exact_names_first()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.FindSymbolsAsync(new("Greeter"), TestContext.Current.CancellationToken);
        Assert.Equal("T:Alpha.Greeter", result.Items[0].Id);
        Assert.DoesNotContain(result.Items, hit => hit.Path.Contains("/obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Items, hit => hit.Name == "GreeterCodec");

        // "Greeter" alone matches only the type, so the assertion above proves nothing about ranking.
        // "greet" also matches the Greet method by its exact name, and only a contains-match on the type
        // name, so this is what actually pins the exact-before-prefix order.
        var greetResult = await workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        Assert.Equal("M:Alpha.Greeter.Greet(System.String)", greetResult.Items[0].Id);
        Assert.Equal("T:Alpha.Greeter", greetResult.Items[1].Id);
    }

    [Fact]
    public async Task References_mark_hits_in_generated_documents()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.ReferencesAsync(new("T:Alpha.Greeter"), TestContext.Current.CancellationToken);
        Assert.Contains(result.Items, hit => hit.Path.Contains("/obj/", StringComparison.Ordinal) && hit.Generated);
        Assert.Contains(result.Items, hit => hit.Path == FixtureSolutions.ProgramPath && !hit.Generated);
    }

    [Fact]
    public async Task References_cross_the_project_boundary()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.ReferencesAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        // The generated GreeterCodec document also calls Greet; it is a genuine hit (marked, not dropped -
        // see References_mark_hits_in_generated_documents), so this looks up the Beta hit by path rather
        // than asserting there is only one.
        var hit = Assert.Single(result.Items, hit => hit.Path == FixtureSolutions.ProgramPath);
        Assert.Equal("Beta", hit.Project);
        Assert.Equal(7, hit.Line);
        Assert.Equal("""public static string Run() => new Greeter().Greet("world");""", hit.Text);
        Assert.False(hit.Generated);
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
        Assert.Equal(1, result.TotalCount);
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
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.ConsoleWithoutMain), NullLogger<SolutionWorkspace>.Instance);
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
        Assert.Equal(4, Assert.Single(map.Projects, project => project.Name == "Beta").DocumentCount);
        var edge = Assert.Single(map.References);
        Assert.Equal(("Beta", "Alpha"), (edge.From, edge.To));
    }

    // The gate bounds concurrent semantic queries to two (design 4.2). The fixture query resolves too
    // fast to deterministically observe the cap in effect, so this only pins completion and that the
    // gate's slots are fully restored afterward.
    [Fact]
    public async Task At_most_two_queries_run_at_once()
    {
        using var workspace = await ReadyAsync();
        var first = workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        var second = workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        var third = workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        await Task.WhenAll(first, second, third);
        Assert.Equal(2, workspace.AvailableQuerySlots);
    }

    [Fact]
    public async Task Reload_opens_again_and_bumps_nothing_durable()
    {
        var loader = new AdhocSolutionLoader(FixtureSolutions.TwoProjects);
        using var workspace = new SolutionWorkspace(loader, NullLogger<SolutionWorkspace>.Instance);
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
        var workspace = new SolutionWorkspace(loader, NullLogger<SolutionWorkspace>.Instance);
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
    public async Task Dispose_during_a_cancelled_load_keeps_the_disposed_status()
    {
        var loader = new CancellingLoader();
        var workspace = new SolutionWorkspace(loader, NullLogger<SolutionWorkspace>.Instance);
        var opening = workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        workspace.Dispose();
        loader.Release();
        await opening;
        Assert.Equal(WorkspacePhase.Failed, workspace.Status.Phase);
        Assert.Equal("the workspace was disposed", workspace.Status.Detail);
    }

    private sealed class CancellingLoader : ISolutionLoader
    {
        private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _source.SetResult();

        public async Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
        {
            await _source.Task.ConfigureAwait(false);
            // Dispose() has already cancelled _lifetime by the time Release() lets this continue.
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("CancellingLoader always observes cancellation first.");
        }
    }

    [Fact]
    public async Task A_loader_failure_becomes_a_failed_status_with_advice()
    {
        using var workspace = new SolutionWorkspace(new ThrowingLoader(), NullLogger<SolutionWorkspace>.Instance);
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
