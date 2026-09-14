using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// code_promote is the whole landing in one tool call: build the standby, promote it, report. The model
// never sees an exception, only a status and, on a broken build, the errors.
public sealed class SlotToolFacts
{
    private sealed record World(
        BrainSimulation Brain,
        NativeTools Tools,
        FakeSlotBuilder Builder,
        FakeSlotEndpoints Endpoints,
        FakeAspireResourceCommands Aspire,
        InMemoryActiveSlotLease Lease) : IAsyncDisposable
    {
        public Task<SlotSnapshot> ReadAsync(string name)
            => Brain.Grains.GetGrain<ISlot>(new NeuronId(CodingVocabulary.SlotType, name).ToGrainId()).Read();

        public ValueTask DisposeAsync() => Brain.DisposeAsync();
    }

    private static async Task<World> StartAsync()
    {
        var builder = new FakeSlotBuilder();
        var endpoints = new FakeSlotEndpoints { ActiveSlot = "a" };
        var aspire = new FakeAspireResourceCommands();
        var lease = new InMemoryActiveSlotLease("a");
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Slot"] = "a",
                ["DigitalBrain:Slots:Grace"] = "00:00:00",
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton<ISlotBuilder>(builder);
                silo.Services.AddSingleton<ISlotEndpoints>(endpoints);
                silo.Services.AddSingleton<IAspireResourceCommands>(aspire);
                silo.Services.AddSingleton<IActiveSlotLease>(lease);
            },
        });
        return new World(brain, brain.SiloServices.GetRequiredService<NativeTools>(), builder, endpoints, aspire, lease);
    }

    private static async Task<JsonElement> PromoteAsync(World world, Dictionary<string, object?> arguments)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(world.Tools.Resolve(["code_promote"])));
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    [Fact]
    public async Task A_promote_builds_the_standby_switches_the_gateway_and_reports_live()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;

        var result = await PromoteAsync(world, new() { ["slot"] = "b" });

        Assert.Equal("Live", result.GetProperty("status").GetString());
        Assert.Equal("b", result.GetProperty("slot").GetString());
        Assert.True(result.GetProperty("rollbackAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("advice").ValueKind);
        // No solution is open in this silo, so there is no repository to read a generation from; the
        // promotion is not blocked by that (GitRunner.HeadCommitAsync has its own facts in Task 5).
        Assert.Equal(JsonValueKind.Null, result.GetProperty("generation").ValueKind);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
        Assert.Equal(("kernel-b", "start"), Assert.Single(world.Aspire.Executed));
    }

    [Fact]
    public async Task A_broken_build_returns_the_errors_and_leaves_the_live_slot_alone()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 3.0, "dotnet build", null);

        var result = await PromoteAsync(world, new() { ["slot"] = "b" });

        Assert.Equal("Failed", result.GetProperty("status").GetString());
        Assert.Equal("CS0103", result.GetProperty("errors")[0].GetProperty("id").GetString());
        Assert.Contains("the live slot is untouched", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_promote_with_an_uncommitted_change_set_is_advice_not_a_build()
    {
        await using var world = await StartAsync();

        var result = await PromoteAsync(world, new() { ["slot"] = "b", ["changeId"] = "never-committed" });

        Assert.Contains("commit it before promoting", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Builder.Builds);
    }

    [Fact]
    public async Task A_rollback_is_refused_after_a_landing_that_changed_persisted_state()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Builder.TouchesSerializedState = true;

        var promoted = await PromoteAsync(world, new() { ["slot"] = "b" });
        Assert.Equal("Live", promoted.GetProperty("status").GetString());
        Assert.False(promoted.GetProperty("rollbackAllowed").GetBoolean());

        var refused = await PromoteAsync(world, new() { ["slot"] = "a", ["promoteOnly"] = true });

        Assert.Contains("forward-only", refused.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Equal("b", world.Lease.Holder);
        Assert.Equal(["b"], world.Endpoints.Switches);
    }

    [Fact]
    public async Task An_unknown_slot_is_advice()
    {
        await using var world = await StartAsync();
        var result = await PromoteAsync(world, new() { ["slot"] = "c" });
        Assert.Contains("is not a slot", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Builder.Builds);
    }

    [Fact]
    public async Task A_slot_named_in_another_case_is_the_same_slot()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;

        var result = await PromoteAsync(world, new() { ["slot"] = "B" });

        // NeuronId lowercases a grain's type but not its name, while the URL, the Aspire resource and the
        // lease all match case-insensitively: an uncanonicalised 'B' would promote b for real and record the
        // phase in a second grain that the rollback rule never reads.
        Assert.Equal("b", result.GetProperty("slot").GetString());
        Assert.Equal("Live", result.GetProperty("status").GetString());
        Assert.Equal(SlotPhase.Live, (await world.ReadAsync("b")).Phase);
    }

    [Fact]
    public async Task A_rollback_does_not_take_a_change_set()
    {
        await using var world = await StartAsync();

        var result = await PromoteAsync(world, new() { ["slot"] = "a", ["promoteOnly"] = true, ["changeId"] = "x" });

        Assert.Contains("Pass one or the other", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Builder.Builds);
        Assert.Empty(world.Endpoints.Switches);
    }

    [Fact]
    public async Task A_promote_of_a_failed_slot_is_advice()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 3.0, "dotnet build", null);
        Assert.Equal("Failed", (await PromoteAsync(world, new() { ["slot"] = "b" })).GetProperty("status").GetString());

        // Failed is terminal: the artifacts and any process behind them are unaccounted for, so the slot is
        // built again rather than promoted as it stands.
        var result = await PromoteAsync(world, new() { ["slot"] = "b", ["promoteOnly"] = true });

        Assert.Contains("failed its last build", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }
}
