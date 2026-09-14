using System.ComponentModel;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// Design 4.4: build the standby, start it, smoke it read-only, flip the lease, wait for the standby to see
// the flip, switch the gateway, let the old slot drain. The commands only validate and schedule; every
// step above is a reaction.
public sealed class SlotNeuronFacts
{
    private sealed record World(
        BrainSimulation Brain,
        FakeSlotBuilder Builder,
        FakeSlotEndpoints Endpoints,
        FakeAspireResourceCommands Aspire,
        InMemoryActiveSlotLease Lease) : IAsyncDisposable
    {
        public ISlot Slot(string name) => Brain.Grains.GetGrain<ISlot>(new NeuronId(CodingVocabulary.SlotType, name).ToGrainId());

        public Task<SlotSnapshot> UntilAsync(string name, Func<SlotSnapshot, bool> done)
            => TestWait.UntilAsync(Slot(name).Read, done, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Brain.DisposeAsync();
    }

    private static async Task<World> StartAsync(
        string liveSlot = "a",
        string leaseSettle = "00:00:10",
        string healthAttempts = "120",
        string switchWait = "00:00:20",
        bool aspireConfigured = true)
    {
        var builder = new FakeSlotBuilder();
        var endpoints = new FakeSlotEndpoints { ActiveSlot = liveSlot };
        var aspire = new FakeAspireResourceCommands();
        var lease = new InMemoryActiveSlotLease(liveSlot);
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Slot"] = liveSlot,
                ["DigitalBrain:Slots:Grace"] = "00:00:00",
                ["DigitalBrain:Slots:LeaseSettle"] = leaseSettle,
                ["DigitalBrain:Slots:HealthAttempts"] = healthAttempts,
                ["DigitalBrain:Slots:HealthPoll"] = "00:00:00.050",
                ["DigitalBrain:Slots:SwitchWait"] = switchWait,
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects));
                silo.Services.AddSingleton<ISlotBuilder>(builder);
                silo.Services.AddSingleton<ISlotEndpoints>(endpoints);
                silo.Services.AddSingleton<IActiveSlotLease>(lease);
                if (aspireConfigured)
                {
                    silo.Services.AddSingleton<IAspireResourceCommands>(aspire);
                }
            },
        });
        return new World(brain, builder, endpoints, aspire, lease);
    }

    [Fact]
    public async Task A_build_records_the_output_root_and_allows_a_rollback()
    {
        await using var world = await StartAsync();
        var accepted = await world.Slot("b").Build(new BuildSlot(CommandId.New(), "c9611543"));
        Assert.Equal("b", accepted.Receipt.Slot);

        var built = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);
        Assert.Equal("c9611543", built.Generation);
        Assert.Equal("E:/repo/artifacts/slot-b", built.ArtifactsPath);
        Assert.True(built.RollbackAllowed);
        Assert.Empty(built.Errors);
        Assert.False(built.HoldsLease);
        Assert.Equal(("b", 0), (Assert.Single(world.Builder.Builds).Slot, Assert.Single(world.Builder.Builds).ChangedFiles.Count));
        Assert.Empty(world.Aspire.Executed);
    }

    [Fact]
    public async Task A_broken_build_fails_the_slot_and_leaves_the_live_one_alone()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 2.5, "dotnet build", null);

        await world.Slot("b").Build(new BuildSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Equal("CS0103", Assert.Single(failed.Errors).Id);
        Assert.Contains("the live slot is untouched", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Aspire.Executed);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_landing_that_changed_persisted_state_forbids_a_rollback()
    {
        await using var world = await StartAsync();
        world.Builder.TouchesSerializedState = true;

        await world.Slot("b").Build(new BuildSlot(CommandId.New(), ChangedFiles: ["E:/repo/src/Kernel/DigitalBrain/Neuron/NeuronState.cs"]));

        var built = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);
        Assert.False(built.RollbackAllowed);
        Assert.Single(Assert.Single(world.Builder.Builds).ChangedFiles);
    }

    [Fact]
    public async Task A_build_that_cannot_even_start_settles_instead_of_retrying_forever()
    {
        await using var world = await StartAsync();
        // ProcessRunner starts dotnet outside its own guard, so a missing dotnet arrives here as a
        // Win32Exception. Anything that escapes the reaction is discarded and retried forever.
        world.Builder.Failure = new Win32Exception("The system cannot find the file specified.");

        await world.Slot("b").Build(new BuildSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("The system cannot find the file specified.", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("the live slot is untouched", failed.Detail, StringComparison.Ordinal);
        Assert.Equal(1, failed.Revision);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task Building_the_live_slot_is_refused()
    {
        await using var world = await StartAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("a").Build(new BuildSlot(CommandId.New())));
        // Windows cannot overwrite the assemblies this process has loaded (R5.4).
        Assert.Contains("serving traffic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_promotion_starts_the_standby_smokes_it_and_flips_the_lease()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.True(live.Healthy);
        Assert.Equal(("kernel-b", "start"), Assert.Single(world.Aspire.Executed));
        Assert.Equal("http://localhost:5082/chats/slot-smoke/brain", Assert.Single(world.Endpoints.Smokes));
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
        // Rollback stays open, so the old slot keeps running behind the fence.
        Assert.Contains("still running", live.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(world.Aspire.Executed, entry => entry.Command == "stop");
    }

    [Fact]
    public async Task A_forward_only_promotion_stops_the_slot_it_replaced()
    {
        await using var world = await StartAsync();
        world.Builder.TouchesSerializedState = true;
        await world.Slot("b").Build(new BuildSlot(CommandId.New(), ChangedFiles: ["E:/repo/src/Kernel/DigitalBrain/Neuron/NeuronState.cs"]));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Contains("forward-only", live.Detail, StringComparison.Ordinal);
        Assert.Contains(("kernel-a", "stop"), world.Aspire.Executed);
    }

    [Fact]
    public async Task A_promotion_waits_for_the_standby_to_report_the_lease_before_moving_traffic()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        // The row is written, but the standby only learns of it on its own next refresh.
        world.Endpoints.LeaseProbesBeforeFlip = 1;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Equal(["b", "b"], world.Endpoints.LeaseProbes);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
    }

    [Fact]
    public async Task A_standby_that_never_reports_the_lease_gets_it_taken_back()
    {
        await using var world = await StartAsync(leaseSettle: "00:00:00");
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.NeverSeesTheLease = true;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("did not report the active lease", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_lease_that_cannot_be_handed_back_is_named_in_the_failure()
    {
        await using var world = await StartAsync(leaseSettle: "00:00:00");
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.NeverSeesTheLease = true;
        var acquires = 0;
        world.Lease.Interleave = () =>
        {
            // Between the hand-back's read and its write the row changes, so the swap loses and the lease
            // stays where the flip put it. Only the flip to 'b' lands.
            if (++acquires == 2)
            {
                world.Lease.Holder = "b";
            }
        };
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("did not report the active lease", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("the lease could not be handed back and is on 'b'", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("promote 'a' or repair the lease row", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("b", world.Lease.Holder);
    }

    [Fact]
    public async Task A_gateway_that_serves_the_old_slot_after_the_switch_hands_the_lease_back()
    {
        await using var world = await StartAsync();
        // The switch is accepted, but the gateway goes on serving 'a': traffic never moved, so the lease
        // belongs back where the traffic is.
        world.Endpoints.SwitchDoesNotTakeEffect = true;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("reports 'a' as active, not 'b'", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("the lease is back with 'a'", failed.Detail, StringComparison.Ordinal);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("a", world.Lease.Holder);
        Assert.DoesNotContain(world.Aspire.Executed, entry => entry.Command == "stop");
    }

    [Fact]
    public async Task A_standby_that_never_answers_health_fails_after_the_configured_attempts()
    {
        await using var world = await StartAsync(healthAttempts: "2");
        world.Endpoints.UnhealthyProbes = int.MaxValue;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("did not answer /health", failed.Detail, StringComparison.Ordinal);
        // Attempt 0 probed too, and it is the attempt that started the resource.
        Assert.Contains("after 2 attempts", failed.Detail, StringComparison.Ordinal);
        Assert.Equal(("kernel-b", "start"), Assert.Single(world.Aspire.Executed));
        Assert.Empty(world.Endpoints.Smokes);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_debounced_gateway_is_waited_out_not_failed()
    {
        // A gateway asking for five seconds is waited out for SwitchWait, not for what it asked.
        await using var world = await StartAsync(switchWait: "00:00:00.500");
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.FromSeconds(5));
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
    }

    [Fact]
    public async Task A_gateway_that_keeps_debouncing_hands_the_lease_back()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("still debouncing switches", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_standby_configured_for_another_slot_is_a_configuration_failure()
    {
        await using var world = await StartAsync();
        // The silo answering slot b's address was given DigitalBrain:Slot=a, so it can never report
        // holding slot b's lease, however long the promotion waits for it.
        world.Endpoints.NamedSlot = "a";
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("names itself 'a'", failed.Detail, StringComparison.Ordinal);
        Assert.Contains(ActiveSlotNames.SlotKey, failed.Detail, StringComparison.Ordinal);
        // The fault is knowable before anything moves, so nothing was smoked, flipped or switched.
        Assert.Empty(world.Endpoints.Smokes);
        Assert.Empty(world.Endpoints.LeaseProbes);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_smoke_read_that_fails_switches_nothing()
    {
        await using var world = await StartAsync();
        world.Endpoints.SmokeFailure = "the smoke read /chats/slot-smoke/brain on http://localhost:5082 answered 500";
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("answered 500", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task An_aspire_failure_is_reported_verbatim()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Aspire.Failure = "The configured Aspire application is unavailable. Check its AppHost and try again.";
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Equal("The configured Aspire application is unavailable. Check its AppHost and try again.", failed.Detail);
        Assert.Empty(world.Endpoints.Switches);
    }

    [Fact]
    public async Task A_silo_that_composed_no_microsoft_module_refuses_the_promotion_with_advice()
    {
        // No fake registered, so this is the real UnconfiguredAspireResourceCommands the Coding module
        // falls back to when no Microsoft module was composed.
        await using var world = await StartAsync(aspireConfigured: false);
        world.Endpoints.UnhealthyProbes = 1;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("Compose the Microsoft module", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("kernel-b", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Aspire.Executed);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task Promoting_a_slot_whose_build_failed_is_refused()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 2.5, "dotnet build", null);
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("b").Promote(new PromoteSlot(CommandId.New())));
        Assert.Contains("failed its last build", error.Message, StringComparison.Ordinal);
        Assert.Empty(world.Aspire.Executed);
        Assert.Empty(world.Endpoints.Switches);
    }

    [Fact]
    public async Task An_untouched_slot_reads_as_idle()
    {
        await using var world = await StartAsync();

        var idle = await world.Slot("b").Read();
        Assert.Equal("b", idle.Slot);
        Assert.Equal(SlotPhase.Idle, idle.Phase);
        Assert.Null(idle.Generation);
        Assert.Null(idle.ArtifactsPath);
        Assert.Null(idle.Detail);
        Assert.Empty(idle.Errors);
        Assert.False(idle.Healthy);
        Assert.False(idle.HoldsLease);
        Assert.True(idle.RollbackAllowed);
        Assert.Equal(0, idle.Revision);

        // Only the silo's own slot answers that it holds the lease; reading it costs no state.
        Assert.True((await world.Slot("a").Read()).HoldsLease);
        Assert.Equal(0, (await world.Slot("a").Read()).Revision);
    }

    [Fact]
    public async Task A_retire_whose_stop_fails_leaves_the_slot_failed_not_retired()
    {
        await using var world = await StartAsync();
        world.Aspire.Failure = "Aspire: resource 'kernel-b' is not known to this application.";

        await world.Slot("b").Retire(new RetireSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Failed or SlotPhase.Retired);
        Assert.Equal(SlotPhase.Failed, failed.Phase);
        Assert.Equal("Aspire: resource 'kernel-b' is not known to this application.", failed.Detail);
        Assert.Equal(("kernel-b", "stop"), Assert.Single(world.Aspire.Executed));
    }

    [Fact]
    public async Task Retiring_stops_the_resource_and_promoting_the_live_slot_is_refused()
    {
        await using var world = await StartAsync();
        await world.Slot("b").Retire(new RetireSlot(CommandId.New()));

        var retired = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Retired);
        Assert.Equal(("kernel-b", "stop"), Assert.Single(world.Aspire.Executed));
        Assert.False(retired.Healthy);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("a").Promote(new PromoteSlot(CommandId.New())));
        Assert.Contains("already holds the active lease", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_slot_name_is_refused()
    {
        await using var world = await StartAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("c").Build(new BuildSlot(CommandId.New())));
        Assert.Contains("'a' or 'b'", error.Message, StringComparison.Ordinal);
    }
}
