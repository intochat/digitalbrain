using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// 04-08 Task 1: TestOS turn semantics, plus the ported os InMemoryRuntimeFacts and DeferFacts behaviours (Watch facts are gone with Phase 4).
public sealed class TurnFacts : IAsyncLifetime
{
    private static readonly BrainId Brain = new("ada");
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);
    private readonly Probe _probe = new();
    private readonly OS _os;

    public TurnFacts()
    {
        _os = OS.Local(options => options.Services = _probe);
        _os.Register(typeof(TurnProbe).Assembly);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _os.DisposeAsync();

    private static NeuronId Id(string type, string key = "one") => new(Brain, type, key);

    // Behaviour 1
    [Fact]
    public async Task Get_activates_the_registered_class_once_per_id_and_calls_through_the_proxy()
    {
        var id = Id("turn.probe");
        var probe = _os.Get<ITurnProbe>(id);

        Assert.IsType<Invocation.NeuronProxy>(probe, exactMatch: false);
        Assert.Equal("hello", await probe.Echo("hello"));
        Assert.Equal("again", await probe.Echo("again"));
        Assert.Equal(1, await probe.Activations());
        Assert.Equal(1, await _os.Get<ITurnProbe>(Id("turn.probe", "two")).Activations());
    }

    // Behaviour 2: one turn at a time, and the neutral attributes interleave.
    [Fact]
    public async Task A_plain_call_waits_for_the_turn_while_Interleaves_and_ReadOnlyCall_run_inside_it()
    {
        var probe = _os.Get<ITurnProbe>(Id("turn.probe"));
        var first = probe.Hold("a");
        await _probe.Reached("a");
        var second = probe.Hold("b");
        Assert.Equal("quick", await probe.Quick("quick"));
        Assert.Equal("look", await probe.Look("look"));
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("start:b", _probe.Log);

        _probe.Open("a");
        _probe.Open("b");
        await Task.WhenAll(first, second);
        Assert.Equal(["start:a", "quick:quick", "look:look", "end:a", "start:b", "end:b"], _probe.Log);
    }

    [Fact]
    public async Task InterleavesWhen_interleaves_exactly_when_its_inherited_predicate_is_true()
    {
        var probe = _os.Get<ITurnProbe>(Id("turn.when"));
        var held = probe.Hold("held");
        await _probe.Reached("held");

        var quick = probe.Hold("quick");
        await _probe.Reached("quick");
        var plain = probe.Hold("plain");
        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("start:plain", _probe.Log);

        _probe.Open("quick");
        _probe.Open("held");
        _probe.Open("plain");
        await Task.WhenAll(held, quick, plain);
        Assert.True(_probe.Log.ToList().IndexOf("start:plain") > _probe.Log.ToList().IndexOf("end:held"));
    }

    [Fact]
    public async Task A_Reentrant_class_interleaves_every_call()
    {
        var probe = _os.Get<ITurnProbe>(Id("turn.reentrant"));
        var first = probe.Hold("a");
        await _probe.Reached("a");
        var second = probe.Hold("b");
        await _probe.Reached("b");

        _probe.Open("a");
        _probe.Open("b");
        await Task.WhenAll(first, second);
        Assert.Equal(["start:a", "start:b"], _probe.Log.Take(2));
    }

    // Behaviour 3
    [Fact]
    public async Task Save_writes_state_and_signal_as_one_journal_entry_and_state_survives_deactivation()
    {
        var id = Id("turn.probe");
        var probe = _os.Get<ITurnProbe>(id);
        await probe.Count(2);
        await probe.Count(3);

        var journal = _os.Journal(Brain);
        Assert.Equal(2, journal.Saves);
        Assert.Equal([new Counted(2), new Counted(5)], journal.Signals(id).Select(entry => entry.Signal));
        Assert.Equal([1L, 2L], journal.Signals(id).Select(entry => entry.Sequence));

        await _os.Deactivate(id);
        Assert.False(_os.IsActive(id));
        Assert.Equal(5, await probe.Total());
        Assert.Equal(2, await probe.Activations());
    }

    // Behaviour 4
    [Fact]
    public async Task Timers_and_reminders_fire_as_turns_on_Advance_and_a_reminder_survives_deactivation()
    {
        var id = Id("turn.probe");
        var probe = _os.Get<ITurnProbe>(id);
        await probe.StartTimers();

        _os.Clock.Advance(TimeSpan.FromSeconds(1));
        await _os.Idle();
        Assert.Equal(["fired:tick"], _probe.Log);

        await _os.Deactivate(id);
        _os.Clock.Advance(TimeSpan.FromSeconds(9));
        await _os.Idle();

        Assert.Equal(["fired:tick", "deactivated:Requested", "fired:remind"], _probe.Log);
        Assert.True(_os.IsActive(id));
        Assert.Equal(2, await probe.Activations());
    }

    // Behaviour 5
    [Fact]
    public async Task Deactivate_runs_OnDeactivate_after_the_current_turn_and_Idle_waits_for_every_turn()
    {
        var id = Id("turn.probe");
        var probe = _os.Get<ITurnProbe>(id);
        var held = probe.Hold("d");
        await _probe.Reached("d");
        var deactivated = _os.Deactivate(id);
        var idle = _os.Idle();

        await Task.Delay(Settle, TestContext.Current.CancellationToken);
        Assert.False(idle.IsCompleted);
        Assert.DoesNotContain("deactivated:Requested", _probe.Log);

        _probe.Open("d");
        await held;
        await deactivated;
        await idle;
        Assert.Equal(["start:d", "end:d", "deactivated:Requested"], _probe.Log);
        Assert.False(_os.IsActive(id));
    }

    // Behaviour 6
    [Fact]
    public async Task Get_of_another_brain_throws_before_activation_and_an_unregistered_type_throws_with_its_name()
    {
        var other = new NeuronId(new BrainId("bob"), "turn.probe", "one");
        var probe = _os.Get<ITurnProbe>(Id("turn.probe"));

        await Assert.ThrowsAsync<ArgumentException>(() => probe.Call(other));
        Assert.False(_os.IsActive(other));

        var error = Assert.Throws<InvalidOperationException>(() => _os.Get<ITurnProbe>(Id("missing.type")));
        Assert.Contains("missing.type", error.Message, StringComparison.Ordinal);
    }

    // Ported: AOneWayCallReturnsAtOnceAndRunsBehindTheTurn
    [Fact]
    public async Task A_one_way_call_returns_at_once_and_runs_behind_the_turn()
    {
        var probe = _os.Get<ITurnProbe>(Id("turn.probe"));
        var held = probe.Hold("w");
        await _probe.Reached("w");

        await probe.Note("n");
        Assert.DoesNotContain("note:n", _probe.Log);

        _probe.Open("w");
        await held;
        await _os.Idle();
        Assert.Equal(["start:w", "end:w", "note:n"], _probe.Log);
    }

    // Ported: ACallCycleFailsAtTheDefaultTimeoutInsteadOfHanging
    [Fact]
    public async Task A_call_cycle_fails_at_the_call_timeout_on_the_TestOS_clock_instead_of_hanging()
    {
        var a = Id("turn.probe", "a");
        var b = Id("turn.probe", "b");
        var cycle = _os.Get<ITurnProbe>(a).Bounce(b);

        for (var step = 0; step < 100 && !cycle.IsCompleted; step++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            _os.Clock.Advance(_os.Options.CallTimeout);
        }

        await Assert.ThrowsAsync<TimeoutException>(() => cycle);
    }

    // Ported: ADeactivationThatThrowsStillReleasesTheTurn
    [Fact]
    public async Task A_deactivation_that_throws_still_releases_the_turn()
    {
        var id = Id("turn.probe");
        var probe = _os.Get<ITurnProbe>(id);
        await probe.Echo("up");
        _probe.ThrowOnDeactivate = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _os.Deactivate(id));
        _probe.ThrowOnDeactivate = false;

        Assert.Equal("next", await probe.Echo("next"));
        Assert.Equal(2, await probe.Activations());
    }

    // Ported: TimersTickOnAdvanceAndStop
    [Fact]
    public async Task A_periodic_timer_ticks_on_each_Advance_and_stops_with_the_activation()
    {
        var id = Id("turn.probe");
        await _os.Get<ITurnProbe>(id).StartTimers();

        _os.Clock.Advance(TimeSpan.FromSeconds(5));
        await _os.Idle();
        _os.Clock.Advance(TimeSpan.FromSeconds(5));
        await _os.Idle();
        Assert.Equal(3, _probe.Log.Count(line => line is "fired:tick" or "fired:every"));

        await _os.Deactivate(id);
        _os.Clock.Advance(TimeSpan.FromSeconds(5));
        await _os.Idle();
        Assert.DoesNotContain(_probe.Log.SkipWhile(line => line != "deactivated:Requested"), line => line == "fired:every");
    }

    // Ported: DeferFacts (the deferred work runs once the turn has ended, so a call back into the neuron does not wait on it)
    [Fact]
    public async Task Deferred_work_runs_after_the_turn_ends()
    {
        await _os.Get<ITurnProbe>(Id("turn.probe")).DeferEcho("x");
        await _os.Idle();

        Assert.Equal(["turn:x", "deferred:x"], _probe.Log);
        Assert.Empty(_os.Failures);
    }
}
