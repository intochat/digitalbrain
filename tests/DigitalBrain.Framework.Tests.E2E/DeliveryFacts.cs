using DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;

namespace DigitalBrain.Framework.Tests.E2E;

/// <summary>Helpers the facts share; they use only <see cref="RunningTarget"/> controls.</summary>
internal static class Facts
{
    public static BrainId Brain() => new("e2e" + Guid.NewGuid().ToString("N")[..12]);

    public static NeuronId Id(BrainId brain, string type, string key) => new(brain, type, key);

    /// <summary>Waits (bounded, real time) until <paramref name="done"/> holds, letting the target drain first.</summary>
    public static async Task Until(RunningTarget run, Func<Task<bool>> done, CancellationToken ct, int seconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            await run.Idle();
            if (await done()) { return; }
            if (DateTime.UtcNow > deadline) { return; }
            await Task.Delay(50, ct);
        }
    }

    /// <summary>Waits until the listener heard <paramref name="count"/> items, then a little longer so a duplicate would show.</summary>
    public static async Task<string[]> Heard(RunningTarget run, NeuronId listener, int count, CancellationToken ct)
    {
        await Until(run, async () => (await run.Get<IListener>(listener).Heard()).Length >= count, ct);
        await Task.Delay(300, ct);
        await run.Idle();
        return await run.Get<IListener>(listener).Heard();
    }

    public static string[] Numbers(params int[] values) => [.. values.Select(value => "n:" + value)];
}

// 04-15 Task 1: every target offers the same controls.
public sealed class TargetControlFacts(AzuriteFixture azurite)
{
    public static TheoryData<string> Targets => FrameworkTarget.All;

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Every_target_offers_restart_deactivate_publish_fault_clock_and_checkpoint(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        var source = Facts.Id(brain, "e2e.source", "controls");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        await run.Get<ISource>(source).Emit(1);
        run.FailNextPublishAfterAppend(source);
        await Assert.ThrowsAnyAsync<Exception>(() => run.Get<ISource>(source).Emit(2));
        await run.Deactivate(source); // a kill ends the activation; the reloaded state holds the faulted append
        Assert.Equal(2, await run.Get<ISource>(source).Count());
        await run.Get<ISource>(source).Emit(3); // exactly the next publish failed
        Assert.Equal(3, await run.Get<ISource>(source).Count());

        await run.Restart(ct);
        Assert.Equal(3, await run.Get<ISource>(source).Count());

        run.Advance(TimeSpan.FromMinutes(5));
        await run.ForceCheckpoint(brain, ct);
        Assert.Equal(3, await run.Get<ISource>(source).Count());
    }
}

// 04-15 Task 2 (RT-06, TEST-01, success criterion 3): one fact set, identical bodies, three targets.
public sealed class DeliveryFacts(AzuriteFixture azurite)
{
    public static TheoryData<string> Targets => FrameworkTarget.All;

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Delivery_a_subscribed_listener_reacts_once_per_signal_in_order(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        for (var i = 1; i <= 5; i++) { await run.Get<ISource>(source).Emit(i); }

        Assert.Equal(Facts.Numbers(1, 2, 3, 4, 5), await Facts.Heard(run, listener, 5, ct));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task RestartAndCatchUp_every_signal_saved_before_a_restart_is_reacted_to_once_and_one_subscription_remains(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        for (var i = 1; i <= 5; i++)
        {
            await run.Get<ISource>(source).Emit(i);
            // Restart at once: the stop may land in the middle of the listener's reaction; its save carries its cursor (R4-09).
            await run.Restart(ct);
        }
        await run.Get<ISource>(source).Emit(6); // the resumed subscription delivers live after the last restart

        Assert.Equal(Facts.Numbers(1, 2, 3, 4, 5, 6), await Facts.Heard(run, listener, 6, ct));
        Assert.Equal(1, await run.SubscriptionCount(listener, source, typeof(Number)));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task StopMidReaction_a_reaction_whose_save_is_durable_never_runs_again(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        // The listener's own reaction save is durable, then its turn dies before anything after the save runs (R4-09): the stop
        // between the reaction's state save and a separate cursor write.
        run.FailNextPublishAfterAppend(listener);
        await run.Get<ISource>(source).Emit(1);
        await Facts.Until(run, async () =>
        {
            await run.Deactivate(listener); // reload the durable state the faulted save left behind
            return (await run.Get<IListener>(listener).Heard()).Length >= 1;
        }, ct);
        await run.Restart(ct);
        await run.Get<ISource>(source).Emit(2);

        Assert.Equal(Facts.Numbers(1, 2), await Facts.Heard(run, listener, 2, ct));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task KillBetweenWriteAndPublish_loses_nothing_after_resume(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        int[] killed = [2, 4, 7];
        for (var i = 1; i <= 7; i++)
        {
            if (killed.Contains(i))
            {
                run.FailNextPublishAfterAppend(source);
                await Assert.ThrowsAnyAsync<Exception>(() => run.Get<ISource>(source).Emit(i));
            }
            else { await run.Get<ISource>(source).Emit(i); }
        }
        await run.Restart(ct);

        Assert.Equal(Facts.Numbers(1, 2, 3, 4, 5, 6, 7), await Facts.Heard(run, listener, 7, ct));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task TurnSemantics_calls_are_turns_interleaving_calls_overlap_and_a_timer_tick_is_a_turn(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId turns = Facts.Id(brain, "e2e.turns", "t"), meeting = Facts.Id(brain, "e2e.turns", "m");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions(), ct);

        // Two Busy calls and a timer tick due while they run: never two at once.
        await run.Get<ITurns>(turns).Arm(500);
        var busy = new[] { run.Get<ITurns>(turns).Busy(), run.Get<ITurns>(turns).Busy() };
        run.Advance(TimeSpan.FromMilliseconds(500));
        await Task.WhenAll(busy);
        await Facts.Until(run, () => Task.FromResult(Turns.Ticks(turns.ToString()) == 1), ct);
        Assert.Equal(1, Turns.Ticks(turns.ToString()));
        Assert.Equal(1, Turns.MostConcurrent(turns.ToString()));

        // Two [Interleaves] calls are inside together.
        var met = await Task.WhenAll(run.Get<ITurns>(meeting).Meet(), run.Get<ITurns>(meeting).Meet());
        Assert.All(met, seen => Assert.Equal(2, seen));
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task ReactivationDedupe_duplicate_references_and_reactivations_still_react_once_per_signal(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(new FrameworkHostOptions { DuplicateDelivery = true }, ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        for (var round = 1; round <= 3; round++)
        {
            await run.Deactivate(listener);
            await run.Deactivate(source); // its next activation re-announces its Last: one more duplicate
            await run.Get<ISource>(source).Emit(round);
            Assert.Equal(Facts.Numbers([.. Enumerable.Range(1, round)]), await Facts.Heard(run, listener, round, ct));
        }
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Gap_a_stale_cursor_behind_the_checkpoint_floor_gets_Gap_then_the_later_signals(string target)
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Facts.Brain();
        NeuronId source = Facts.Id(brain, "e2e.source", "s"), listener = Facts.Id(brain, "e2e.listener", "l");
        var options = new FrameworkHostOptions { HistoryWindowSignals = 2, HistoryWindowAge = TimeSpan.FromMinutes(1), StaleCursorAfter = TimeSpan.FromMinutes(1) };
        await using var run = await FrameworkTarget.For(target, azurite).StartHost(options, ct);

        await run.Get<IListener>(listener).Follow(source.ToString());
        await run.Get<ISource>(source).Emit(1);
        Assert.Equal(Facts.Numbers(1), await Facts.Heard(run, listener, 1, ct));

        // The listener sleeps at cursor 1 while five more signals are written and never announced.
        await run.Deactivate(listener);
        for (var i = 2; i <= 6; i++)
        {
            run.FailNextPublishAfterAppend(source);
            await Assert.ThrowsAnyAsync<Exception>(() => run.Get<ISource>(source).Emit(i));
        }
        run.Advance(TimeSpan.FromMinutes(2));
        await run.ForceCheckpoint(brain, ct); // floor = 6 - 2 = 4; the stale cursor no longer pins it

        Assert.Equal(["n:1", "gap:1-4", "n:5", "n:6"], await Facts.Heard(run, listener, 4, ct));
    }
}
