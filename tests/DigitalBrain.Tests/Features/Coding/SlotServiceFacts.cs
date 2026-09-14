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

    private static SlotOptions Options(params (string Key, string Value)[] configured)
        => SlotOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(configured.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build());

    [Fact]
    public void Slot_options_default_to_the_gateway_and_the_two_kernel_ports()
    {
        var options = Options();
        Assert.Equal(["a", "b"], SlotOptions.Names);
        Assert.Equal("http://localhost:5080", options.GatewayUrl);
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
        Assert.Equal("http://localhost:5082", options.UrlFor("b"));
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
        Assert.Equal("http://gateway:9000", options.GatewayUrl);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Grace);
        Assert.Equal(7, options.HealthAttempts);
        Assert.Equal("http://kernel-b:6000", options.UrlFor("b"));
        Assert.Equal("silo-b", options.ResourceFor("b"));
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
    }

    [Fact]
    public void An_unknown_slot_is_named_in_the_refusal()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Options().UrlFor("c"));
        Assert.Contains("DigitalBrain:Slots:c:Url", error.Message, StringComparison.Ordinal);
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
    public async Task A_broken_slot_build_carries_the_errors()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, "E:\\repo\\src\\A\\Thing.cs(12,9): error CS0103: The name 'Nope' does not exist [E:\\repo\\src\\A\\A.csproj]");
        var builder = await BuilderAsync(processes);

        var result = await builder.BuildAsync("b", [], TestContext.Current.CancellationToken);

        Assert.False(result.Build.Succeeded);
        Assert.Equal("CS0103", Assert.Single(result.Build.Errors).Id);
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
        // A path the snapshot does not know (deleted, or outside the solution) is not evidence of a state change.
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, ["E:/fixture/Delta/Gone.cs"], TestContext.Current.CancellationToken));
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [], TestContext.Current.CancellationToken));
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

    private static async Task<SlotBuilder> BuilderAsync(FakeProcessRunner processes)
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return new SlotBuilder(new DotnetRunner(processes), workspace, new ChangeSetEditor(new CodeFixCatalog()), Options());
    }
}
