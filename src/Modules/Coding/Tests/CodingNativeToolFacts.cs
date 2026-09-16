using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodingNativeToolFacts
{
    private static async Task<(BrainSimulation Brain, NativeTools Tools, DiskFixture Fixture, FakeProcessRunner Dotnet)> StartAsync(
        CodingToolOptions? options = null, bool configureTestProject = false, Func<DiskFixture, ISolutionLoader>? loader = null)
    {
        var fixture = DiskFixture.Create();
        await fixture.InitGitAsync(TestContext.Current.CancellationToken);
        var dotnet = new FakeProcessRunner();
        var configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = fixture.SolutionPath };
        if (configureTestProject)
        {
            // The fake runner never actually executes this path; it only has to exist so the tool passes
            // a test-project path (not the solution) to DotnetRunner.TestAsync, the same as the live kernel.
            configuration[CodingModule.TestProjectKey] = fixture.Root + "/Beta/Beta.csproj";
        }

        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(loader is null ? new AdhocSolutionLoader(fixture.Open) : loader(fixture));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton(new DotnetRunner(dotnet));
                if (options is not null)
                {
                    silo.Services.AddSingleton(options);
                }
            },
            Configuration = configuration,
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, brain.SiloServices.GetRequiredService<NativeTools>(), fixture, dotnet);
    }

    private static async Task<JsonElement> InvokeAsync(NativeTools tools, string name, Dictionary<string, object?> arguments)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools.Resolve([name])));
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    [Fact]
    public async Task Find_symbols_returns_the_envelope()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var result = await InvokeAsync(tools, "code_find_symbols", new() { ["query"] = "Greeter", ["limit"] = 10 });
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("T:Alpha.Greeter", result.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task References_with_a_bad_id_return_advice()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var result = await InvokeAsync(tools, "code_references", new() { ["symbolId"] = "T:Nope" });
        Assert.Contains("find-symbols", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_report_the_broken_file()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var result = await InvokeAsync(tools, "code_diagnostics", new() { ["path"] = fixture.BrokenPath });
        Assert.Equal(1, result.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Map_is_a_graph_result_the_shell_can_open()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var result = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal("graph", result.GetProperty("kind").GetString());
        var id = result.GetProperty("id").GetString();
        Assert.StartsWith("map-", id, StringComparison.Ordinal);
        Assert.Equal(12, id!.Length);
        Assert.Equal(id, result.GetProperty("name").GetString());
        var again = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal(id, again.GetProperty("id").GetString());
        var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.All(nodes, node => Assert.Equal("module", node.GetProperty("kind").GetString()));
        var edge = Assert.Single(result.GetProperty("edges").EnumerateArray());
        Assert.Equal("Beta", edge.GetProperty("sourceId").GetString());
        Assert.Equal("Alpha", edge.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task The_fourteen_tools_resolve()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        Assert.Equal(14, tools.Resolve(["code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_derived", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test"]).Count());
    }

    [Fact]
    public async Task Skeleton_member_callers_and_implementations_answer_from_the_snapshot()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var skeleton = await InvokeAsync(tools, "code_skeleton", new() { ["path"] = fixture.GreeterPath });
        Assert.Equal(3, skeleton.GetProperty("members").GetArrayLength());
        var member = await InvokeAsync(tools, "code_member", new() { ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)" });
        Assert.Contains("Hello", member.GetProperty("source").GetString(), StringComparison.Ordinal);
        var callers = await InvokeAsync(tools, "code_callers", new() { ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)" });
        // The generated GreeterCodec document also calls Greet and sorts first by path ("Alpha/obj/..."
        // precedes "Beta/Program.cs"); callers is not in design 9.1's query-hygiene scope, so both are real hits.
        Assert.Equal(2, callers.GetProperty("totalCount").GetInt32());
        Assert.Equal("M:Beta.Program.Run", callers.GetProperty("items")[1].GetProperty("id").GetString());
        var implementations = await InvokeAsync(tools, "code_implementations", new() { ["symbolId"] = "T:Alpha.IWelcome" });
        // Shouter also implements IWelcome by inheriting Greeter, so the interface type has two implementers
        // (WorkspaceReadFacts.Derived_of_an_interface_are_its_implementing_types pins the same pair by name).
        Assert.Equal(2, implementations.GetProperty("totalCount").GetInt32());
        var derived = await InvokeAsync(tools, "code_derived", new() { ["symbolId"] = "T:Alpha.Greeter" });
        Assert.Equal(1, derived.GetProperty("totalCount").GetInt32());
        Assert.Equal("T:Beta.Shouter", derived.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Map_answers_from_the_durable_cache_while_a_reload_is_in_flight()
    {
        var gate = new TaskCompletionSource();
        var (brain, tools, fixture, _) = await StartAsync(loader: disk => new GatedSecondOpenLoader(disk.Open, gate));
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        Assert.Equal("graph", (await InvokeAsync(tools, "code_map", new())).GetProperty("kind").GetString());

        // The mapping reaction that saves LastMap runs in its own turn after the open above, so it can still
        // be in flight here; reloading before it lands would have the loop below race a cache that never got
        // written. Wait for the open's generation bump and a live map before moving the workspace to Opening.
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "digitalbrain").ToGrainId());
        await TestWait.UntilAsync(
            async () => (await workspace.Read()).Generation >= 1 && (await InvokeAsync(tools, "code_map", new())).TryGetProperty("kind", out _),
            static warm => warm,
            TimeSpan.FromSeconds(20),
            TestContext.Current.CancellationToken);

        // The gated second open keeps the service Opening until the gate is released below. The tool now asks
        // the workspace grain, which answers a reload in flight from its durable LastMap, so the map keeps
        // coming back; asking the live service directly would be a "still opening" advice instead.
        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        JsonElement cached;
        while (true)
        {
            var snapshot = await workspace.Read();
            cached = await InvokeAsync(tools, "code_map", new());
            if (snapshot.Phase == WorkspacePhase.Opening && cached.TryGetProperty("kind", out _))
            {
                break;
            }

            await Task.Delay(25, timeout.Token);
        }

        Assert.Equal("graph", cached.GetProperty("kind").GetString());
        Assert.Equal(2, cached.GetProperty("nodes").GetArrayLength());

        gate.SetResult();
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Propose_check_and_commit_land_a_change_on_a_coding_branch()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var proposed = await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t1",
            ["kind"] = "Rename",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["newName"] = "Hello",
        });
        Assert.Equal("Draft", proposed.GetProperty("status").GetString());
        Assert.Equal(1, proposed.GetProperty("edits").GetArrayLength());

        var checkedResult = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t1" });
        Assert.Equal("Checked", checkedResult.GetProperty("status").GetString());
        Assert.Contains("+", checkedResult.GetProperty("diff").GetString(), StringComparison.Ordinal);

        var committed = await InvokeAsync(tools, "code_commit", new() { ["changeId"] = "t1", ["message"] = "rename Greet to Hello" });
        Assert.Equal("Committed", committed.GetProperty("status").GetString());
        Assert.Equal("coding/t1", committed.GetProperty("branch").GetString());
        Assert.Equal("main", committed.GetProperty("baseBranch").GetString());
        Assert.Equal(40, committed.GetProperty("commit").GetString()!.Length);
        // Greeter.cs, Program.cs and the generated GreeterCodec document (which also calls Greet) all change.
        Assert.Equal(3, committed.GetProperty("files").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, committed.GetProperty("advice").ValueKind);
        Assert.Contains(""".Hello("world")""", await File.ReadAllTextAsync(fixture.ProgramPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var log = await new ProcessRunner().RunAsync("git", ["log", "-1", "--format=%s"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("coding: rename Greet to Hello", log.Output.Trim());
    }

    [Fact]
    public async Task A_check_that_fails_reports_the_edit_and_the_diagnostics()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t2",
            ["kind"] = "ReplaceMember",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["source"] = "public string Greet(string name) => 42;",
        });
        var checkedResult = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t2" });
        Assert.Equal("Draft", checkedResult.GetProperty("status").GetString());
        Assert.StartsWith("edit 1", checkedResult.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("CS0029", checkedResult.GetProperty("diagnostics")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_unknown_edit_kind_is_advice()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        var result = await InvokeAsync(tools, "code_propose_edit", new() { ["changeId"] = "t3", ["kind"] = "Explode" });
        Assert.Contains("ReplaceMember", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_and_test_return_the_parsed_outcomes()
    {
        var (brain, tools, fixture, dotnet) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        dotnet.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var build = await InvokeAsync(tools, "code_build", new());
        Assert.True(build.GetProperty("succeeded").GetBoolean());
        Assert.Contains(fixture.SolutionPath.Replace('\\', '/'), build.GetProperty("command").GetString()!.Replace('\\', '/'), StringComparison.Ordinal);

        dotnet.Enqueue(0, "Test run summary: Passed!\n  total: 3\n  failed: 0\n  succeeded: 3\n  skipped: 0\n");
        var tests = await InvokeAsync(tools, "code_test", new() { ["filterClass"] = "Some.Class" });
        Assert.True(tests.GetProperty("succeeded").GetBoolean());
        Assert.Equal(3, tests.GetProperty("passed").GetInt32());
        Assert.Contains("--filter-class Some.Class", tests.GetProperty("command").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_and_test_share_one_artifacts_root_rooted_at_the_solution()
    {
        // TestProject points DotnetRunner.TestAsync at the Beta project directory, not the solution
        // directory; a relative artifactsPath rooted there instead of at the solution would name a
        // second, different folder than the one code_build just used for the identical argument.
        var (brain, tools, fixture, dotnet) = await StartAsync(configureTestProject: true);
        using var fixtureScope = fixture;
        await using var brainScope = brain;

        dotnet.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        await InvokeAsync(tools, "code_build", new() { ["artifactsPath"] = "artifacts/x" });
        var buildArtifacts = Assert.Single(dotnet.Calls[^1].Arguments, static argument => argument.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));

        dotnet.Enqueue(0, "Test run summary: Passed!\n  total: 0\n  failed: 0\n  succeeded: 0\n  skipped: 0\n");
        await InvokeAsync(tools, "code_test", new() { ["artifactsPath"] = "artifacts/x" });
        var testArtifacts = Assert.Single(dotnet.Calls[^1].Arguments, static argument => argument.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));

        var expected = "-p:ArtifactsPath=" + fixture.Root + "/artifacts/x";
        Assert.Equal(expected, buildArtifacts.Replace('\\', '/'));
        Assert.Equal(expected, testArtifacts.Replace('\\', '/'));
    }

    [Fact]
    public async Task A_check_that_cannot_get_a_query_slot_settles_as_a_draft()
    {
        var (brain, tools, fixture, _) = await StartAsync(new CodingToolOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)));
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t4",
            ["kind"] = "Rename",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["newName"] = "Hello",
        });

        // Occupying both of the workspace's concurrent query slots keeps the check reaction's own
        // workspace.QueryAsync call from ever starting. The reaction's one-second edit deadline is what
        // settles it - as a Draft whose detail says the check did not finish - rather than the tool's much
        // longer wait expiring on a reaction that never reached SaveAsync at all.
        var workspace = brain.SiloServices.GetRequiredService<SolutionWorkspace>();
        var release = new TaskCompletionSource();
        var occupy1 = workspace.QueryAsync(async (_, token) =>
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            return 0;
        }, TestContext.Current.CancellationToken);
        var occupy2 = workspace.QueryAsync(async (_, token) =>
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
            return 0;
        }, TestContext.Current.CancellationToken);
        try
        {
            var result = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t4" });
            Assert.Equal("Draft", result.GetProperty("status").GetString());
            Assert.Contains("did not finish within 1s", result.GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.False(result.TryGetProperty("advice", out _));
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(occupy1, occupy2);
        }
    }

    [Fact]
    public async Task A_repeated_check_answers_again_without_waiting()
    {
        var (brain, tools, fixture, _) = await StartAsync(new CodingToolOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90)));
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t5",
            ["kind"] = "ReplaceMember",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["source"] = "public string Greet(string name) => 42;",
        });
        var first = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t5" });
        Assert.Equal("CS0029", first.GetProperty("diagnostics")[0].GetProperty("id").GetString());

        // No propose in between: the second check reproduces the exact same failure, so the old Detail-based
        // wait (round 1) could not tell "no new result yet" from "the same result again" and stalled for the
        // full deadline. Revision bumps on every settle regardless, so this answers immediately either way.
        var clock = Stopwatch.StartNew();
        var second = await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t5" });
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"the repeated check took {clock.Elapsed}; a 30s options.ReactionWait means the old stall would fail this.");
        Assert.Equal("CS0029", second.GetProperty("diagnostics")[0].GetProperty("id").GetString());
        Assert.False(second.TryGetProperty("advice", out _));
    }

    [Fact]
    public async Task A_dirty_tree_outside_the_change_set_is_refused_before_any_branch_is_created()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        await InvokeAsync(tools, "code_propose_edit", new()
        {
            ["changeId"] = "t6",
            ["kind"] = "Rename",
            ["symbolId"] = "M:Alpha.Greeter.Greet(System.String)",
            ["newName"] = "Hello",
        });
        await InvokeAsync(tools, "code_check", new() { ["changeId"] = "t6" });

        // Dirty a file the change set never touches. The grain still writes its files and closes the change
        // set, but the git step is refused before the branch is ensured, so the tree stays where it was.
        await File.AppendAllTextAsync(fixture.UnusedPath, "// dirty", TestContext.Current.CancellationToken);

        var committed = await InvokeAsync(tools, "code_commit", new() { ["changeId"] = "t6", ["message"] = "rename Greet to Hello" });
        Assert.Equal("Committed", committed.GetProperty("status").GetString());
        Assert.True(committed.GetProperty("files").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Null, committed.GetProperty("branch").ValueKind);
        Assert.Equal("main", committed.GetProperty("baseBranch").GetString());
        Assert.Equal(JsonValueKind.Null, committed.GetProperty("commit").ValueKind);
        Assert.Contains("outside the change set", committed.GetProperty("advice").GetString(), StringComparison.Ordinal);

        var branches = await new ProcessRunner().RunAsync("git", ["branch", "--list", "coding/t6"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, branches.Output.Trim());
        var current = await new ProcessRunner().RunAsync("git", ["branch", "--show-current"], fixture.Root, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("main", current.Output.Trim());
    }
}
