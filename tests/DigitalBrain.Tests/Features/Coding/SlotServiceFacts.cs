using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SlotServiceFacts
{
    private const string StatePath = "E:/fixture/Delta/SlotState.cs";
    private const string PlainPath = "E:/fixture/Delta/Plain.cs";
    private const string TallyPath = "E:/fixture/Delta/Tally.cs";
    private const string TallySerializedPath = "E:/fixture/Delta/Tally.Serialized.cs";

    private const string StateSource = """
        namespace Delta;

        [GenerateSerializer]
        [Alias("delta.state")]
        public sealed record Counted(int Total);
        """;

    private const string PlainSource = """
        namespace Delta;

        public static class Plain
        {
            public static int Twice(int value) => value * 2;
        }
        """;

    // The half a landing is likely to edit: members, no attributes.
    private const string TallySource = """
        namespace Delta;

        public sealed partial class Tally
        {
            public int Total { get; set; }
        }
        """;

    // The half that makes Tally serialized state, in a file the landing never touched.
    private const string TallySerializedSource = """
        namespace Delta;

        [GenerateSerializer]
        [Alias("delta.tally")]
        public sealed partial class Tally
        {
        }
        """;

    private static SlotOptions Options(params (string Key, string Value)[] configured)
        => SlotOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(configured.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build());

    [Fact]
    public void Slot_options_default_to_the_gateway_and_the_two_kernel_ports()
    {
        var options = Options();
        Assert.Equal(["a", "b"], SlotOptions.Names);
        Assert.Equal(new Uri("http://localhost:5080"), options.GatewayUrl);
        Assert.Equal(new Uri("http://localhost:5081"), options.UrlFor("a"));
        Assert.Equal(new Uri("http://localhost:5082"), options.UrlFor("b"));
        Assert.Equal("kernel-a", options.ResourceFor("a"));
        Assert.Equal("kernel-b", options.ResourceFor("b"));
        Assert.Equal(TimeSpan.FromSeconds(10), options.Grace);
        Assert.Equal(TimeSpan.FromMinutes(20), options.PromoteWait);
        // Long enough for several of the interval the lease refresher polls on.
        Assert.Equal(TimeSpan.FromSeconds(10), options.LeaseSettle);
        Assert.True(options.LeaseSettle >= ActiveSlotNames.RefreshInterval * 2);
        Assert.Equal("/chats/slot-smoke/brain", options.SmokePath);
        Assert.Equal(120, options.HealthAttempts);
        Assert.Null(options.Slot);
    }

    [Fact]
    public void Configured_slots_win_over_the_defaults()
    {
        var options = Options(
            ("DigitalBrain:Slot", "b"),
            ("DigitalBrain:Slots:Gateway", "http://gateway:9000"),
            ("DigitalBrain:Slots:Grace", "00:00:02"),
            ("DigitalBrain:Slots:HealthAttempts", "7"),
            ("DigitalBrain:Slots:b:Url", "http://kernel-b:6000"),
            ("DigitalBrain:Slots:b:Resource", "silo-b"));
        Assert.Equal("b", options.Slot);
        Assert.Equal(new Uri("http://gateway:9000"), options.GatewayUrl);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Grace);
        Assert.Equal(7, options.HealthAttempts);
        Assert.Equal(new Uri("http://kernel-b:6000"), options.UrlFor("b"));
        Assert.Equal("silo-b", options.ResourceFor("b"));
        Assert.Equal(new Uri("http://localhost:5081"), options.UrlFor("a"));
    }

    [Fact]
    public void An_unknown_slot_is_named_in_the_refusal()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Options().UrlFor("c"));
        Assert.Contains("DigitalBrain:Slots:c:Url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_that_is_not_http_is_refused_with_its_key()
    {
        // "localhost:5082" parses as an absolute URI whose scheme is "localhost", so a probe would be the
        // first thing to notice the missing scheme; configuration is.
        var error = Assert.Throws<InvalidOperationException>(() => Options(("DigitalBrain:Slots:b:Url", "localhost:5082")));
        Assert.Contains("DigitalBrain:Slots:b:Url", error.Message, StringComparison.Ordinal);
        Assert.Contains("localhost:5082", error.Message, StringComparison.Ordinal);

        var gateway = Assert.Throws<InvalidOperationException>(() => Options(("DigitalBrain:Slots:Gateway", "not a url")));
        Assert.Contains("DigitalBrain:Slots:Gateway", gateway.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_duration_or_count_is_refused_with_its_key()
    {
        var duration = Assert.Throws<InvalidOperationException>(() => Options(("DigitalBrain:Slots:Grace", "30s")));
        Assert.Contains("DigitalBrain:Slots:Grace", duration.Message, StringComparison.Ordinal);
        Assert.Contains("30s", duration.Message, StringComparison.Ordinal);

        // Zero health attempts would make the promotion give up before its first probe.
        var count = Assert.Throws<InvalidOperationException>(() => Options(("DigitalBrain:Slots:HealthAttempts", "0")));
        Assert.Contains("DigitalBrain:Slots:HealthAttempts", count.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_artifacts_path_is_absolute_under_the_solution()
    {
        var artifacts = Options().ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/artifacts/slot-b", artifacts.Replace('\\', '/'));
        var configured = Options(("DigitalBrain:Slots:ArtifactsRoot", "out")).ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/out/slot-b", configured.Replace('\\', '/'));
    }

    [Fact]
    public async Task A_slot_build_asks_dotnet_for_the_artifacts_layout()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var builder = await BuilderAsync(processes);

        var result = await builder.BuildAsync("b", [], TestContext.Current.CancellationToken);

        Assert.True(result.Build.Succeeded);
        Assert.Equal("E:/fixture/artifacts/slot-b", result.ArtifactsPath.Replace('\\', '/'));
        Assert.False(result.TouchesSerializedState);
        var call = Assert.Single(processes.Calls);
        var arguments = call.Arguments.Select(static argument => argument.Replace('\\', '/')).ToArray();
        Assert.Contains("-p:ArtifactsPath=E:/fixture/artifacts/slot-b", arguments);
        // Without this the SDK keeps bin/obj where they are and the slot's dll is not where the AppHost
        // looks for it (spike S1).
        Assert.Contains("-p:UseArtifactsOutput=true", arguments);
    }

    [Fact]
    public async Task A_broken_slot_build_carries_the_errors_and_scans_nothing()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, "E:\\repo\\src\\A\\Thing.cs(12,9): error CS0103: The name 'Nope' does not exist [E:\\repo\\src\\A\\A.csproj]");
        var builder = await BuilderAsync(processes);

        // A path the snapshot does not know would be a rollback refusal, but a build that failed is never
        // promoted, so the verdict is not computed at all.
        var result = await builder.BuildAsync("b", ["E:/fixture/Delta/Gone.cs"], TestContext.Current.CancellationToken);

        Assert.False(result.Build.Succeeded);
        Assert.Equal("CS0103", Assert.Single(result.Build.Errors).Id);
        Assert.False(result.TouchesSerializedState);
    }

    [Fact]
    public async Task A_written_file_that_declares_a_serialized_state_type_forbids_rollback()
    {
        using var workspace = new AdhocWorkspace();
        var project = ProjectId.CreateNewId("Delta");
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Delta", "Delta", LanguageNames.CSharp,
                filePath: "E:/fixture/Delta/Delta.csproj"))
            .AddDocument(DocumentId.CreateNewId(project), "SlotState.cs", SourceText.From(StateSource), filePath: StatePath)
            .AddDocument(DocumentId.CreateNewId(project), "Plain.cs", SourceText.From(PlainSource), filePath: PlainPath);
        Assert.True(workspace.TryApplyChanges(solution));
        var editor = new ChangeSetEditor(new CodeFixCatalog());

        Assert.True(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [StatePath], TestContext.Current.CancellationToken));
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [PlainPath], TestContext.Current.CancellationToken));
        // A path the snapshot no longer knows (the landing deleted it) cannot be inspected, so the
        // conservative answer is the only safe one: no rollback.
        Assert.True(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, ["E:/fixture/Delta/Gone.cs"], TestContext.Current.CancellationToken));
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_partial_state_type_is_seen_from_the_half_that_carries_no_attribute()
    {
        using var workspace = new AdhocWorkspace();
        var project = ProjectId.CreateNewId("Delta");
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Delta", "Delta", LanguageNames.CSharp,
                filePath: "E:/fixture/Delta/Delta.csproj"))
            .AddDocument(DocumentId.CreateNewId(project), "Tally.cs", SourceText.From(TallySource), filePath: TallyPath)
            .AddDocument(DocumentId.CreateNewId(project), "Tally.Serialized.cs", SourceText.From(TallySerializedSource), filePath: TallySerializedPath);
        Assert.True(workspace.TryApplyChanges(solution));
        var editor = new ChangeSetEditor(new CodeFixCatalog());

        // The landing wrote the members; the attribute sits on the other declaration of the same type.
        Assert.True(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [TallyPath], TestContext.Current.CancellationToken));
        Assert.True(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [TallySerializedPath], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_slot_build_reports_the_rollback_verdict_of_the_files_it_landed()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var builder = await BuilderAsync(processes);

        // Greeter.cs declares no [GenerateSerializer] type, so a landing that wrote it may be rolled back.
        var result = await builder.BuildAsync("b", [FixtureSolutions.GreeterPath], TestContext.Current.CancellationToken);

        Assert.False(result.TouchesSerializedState);
    }

    [Fact]
    public async Task The_generation_a_slot_is_built_from_is_the_head_commit()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "c9611543f0a1b2c3d4e5f60718293a4b5c6d7e8f\n");
        var git = new GitRunner(processes);

        var head = await git.HeadCommitAsync("E:/repo", TestContext.Current.CancellationToken);

        Assert.Equal("c9611543f0a1b2c3d4e5f60718293a4b5c6d7e8f", head);
        Assert.Equal(["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.quotepath=false", "rev-parse", "HEAD"],
            Assert.Single(processes.Calls).Arguments);
    }

    [Fact]
    public async Task A_tree_that_is_not_a_repository_has_no_generation()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(128, string.Empty, "fatal: not a git repository");
        var git = new GitRunner(processes);

        // Not an error: the slot is then built from an unrecorded generation.
        Assert.Null(await git.HeadCommitAsync("E:/repo", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_answer_that_is_not_a_commit_hash_is_refused()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "HEAD detached at v1.2\n");
        var git = new GitRunner(processes);

        // Recording a generation that is not a commit would leave a slot claiming a generation no rollback
        // could ever check out.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => git.HeadCommitAsync("E:/repo", TestContext.Current.CancellationToken));
        Assert.Contains("HEAD detached at v1.2", error.Message, StringComparison.Ordinal);
    }

    private static async Task<SlotBuilder> BuilderAsync(FakeProcessRunner processes)
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return new SlotBuilder(new DotnetRunner(processes), workspace, new ChangeSetEditor(new CodeFixCatalog()), Options());
    }
}
