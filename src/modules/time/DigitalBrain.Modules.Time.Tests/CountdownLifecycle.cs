using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed partial class CountdownLifecycle : CountdownTest
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);
    private static readonly TimeSpan HalfHour = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact(DisplayName =
        "Start from Unscheduled is idempotent under the same CommandId and rejects a second Start")]
    public async Task StartIsIdempotentAndAllowedOnlyFromUnscheduled()
    {
        var test = await BrainAsync();
        var (countdown, destination) = await PairAsync();
        var empty = await countdown.Reference.Read();

        Assert.Equal(CountdownStatus.Unscheduled, empty.Status);
        Assert.Equal(0, empty.Generation);
        Assert.Equal(0, empty.Revision);
        Assert.Null(empty.Destination);

        var command = new StartCountdown(CommandId.New(), Hour, destination.Id);
        var started = await countdown.Reference.Start(command);
        var repeated = await countdown.Reference.Start(command);

        Assert.Equal(started, repeated);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(1, started.Generation);
        Assert.Equal(1, started.Revision);
        Assert.Equal(destination.Id, started.Destination);
        Assert.Equal(test.Clock.UtcNow, started.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + Hour, started.DueAt);
        Assert.Equal(Hour, started.Duration);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(CommandId.New(), Hour, destination.Id)));
    }

    [Fact(DisplayName =
        "Reschedule requires the exact revision and invalidates the prior wakeup")]
    public async Task RescheduleUsesTheExactRevisionAndInvalidatesThePriorWakeup()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(Hour);

        await test.Clock.AdvanceAsync(HalfHour, cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                ExpectedRevision: started.Revision + 1,
                Hour)));

        var rescheduled = await countdown.Reference.Reschedule(
            new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                Hour));

        Assert.Equal(CountdownStatus.Scheduled, rescheduled.Status);
        Assert.Equal(started.Generation, rescheduled.Generation);
        Assert.Equal(started.Revision + 1, rescheduled.Revision);
        Assert.Equal(started.Destination, rescheduled.Destination);
        Assert.Equal(test.Clock.UtcNow, rescheduled.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + Hour, rescheduled.DueAt);

        await test.Clock.AdvanceAsync(HalfHour, cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(HalfHour, cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);

        Assert.Equal(rescheduled.Generation, elapsed.Synapse.Generation);
        Assert.Equal(rescheduled.Revision, elapsed.Synapse.Revision);
    }

    [Fact(DisplayName =
        "Cancel requires the exact revision, is idempotent, and is terminal without emission")]
    public async Task CancelUsesTheExactRevisionAndIsTerminal()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(Hour);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(CommandId.New(), ExpectedRevision: started.Revision + 1)));

        var command = new CancelCountdown(CommandId.New(), started.Revision);
        var cancelled = await countdown.Reference.Cancel(command);
        var repeated = await countdown.Reference.Cancel(command);

        Assert.Equal(cancelled, repeated);
        Assert.Equal(CountdownStatus.Cancelled, cancelled.Status);
        Assert.Equal(started.Generation, cancelled.Generation);
        Assert.Equal(started.Revision, cancelled.Revision);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(CommandId.New(), cancelled.Revision)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(CommandId.New(), cancelled.Revision, Hour)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(CommandId.New(), Hour, destination.Id)));

        await test.Clock.AdvanceAsync(TwoHours, cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName =
        "Restart after Cancel retains destination and starts a new generation")]
    public async Task RestartRetainsDestinationAndStartsANewGeneration()
    {
        var test = await BrainAsync();
        var (countdown, _, started) = await ScheduleAsync(Hour);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Restart(new RestartCountdown(CommandId.New(), Hour)));

        var cancelled = await countdown.Reference.Cancel(new CancelCountdown(CommandId.New(), started.Revision));
        var restarted = await countdown.Reference.Restart(new RestartCountdown(CommandId.New(), TwoHours));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(cancelled.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(cancelled.Destination, restarted.Destination);
        Assert.Equal(test.Clock.UtcNow, restarted.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TwoHours, restarted.DueAt);
        Assert.Equal(TwoHours, restarted.Duration);
    }

    [Fact(DisplayName = "Restart is allowed after Elapsed and starts a new generation")]
    public async Task RestartIsAllowedAfterElapsed()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(Hour);

        await test.Clock.AdvanceAsync(Hour, cancellationToken);
        _ = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);

        var restarted = await countdown.Reference.Restart(new RestartCountdown(CommandId.New(), TwoHours));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(started.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(started.Destination, restarted.Destination);
    }

    [Fact(DisplayName = "Countdown emits CountdownElapsed exactly once at its due instant")]
    public async Task CountdownEmitsExactlyOnceAtItsDueInstant()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(Hour);

        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(59), cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(OneMinute, cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);

        Assert.Equal(countdown.Id, elapsed.Synapse.Countdown);
        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(destination.Id, elapsed.Synapse.Destination);
        Assert.Equal(started.ScheduledAt, elapsed.Synapse.ScheduledAt);
        Assert.Equal(started.DueAt, elapsed.Synapse.DueAt);
        Assert.Equal(test.Clock.UtcNow, elapsed.Synapse.ObservedAt);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);
        Assert.Equal(CountdownStatus.Elapsed, (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(OneMinute, cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "Read returns the committed snapshot after a hosting silo restart")]
    public async Task ReadReturnsCommittedStateAfterHostingSiloRestart()
    {
        var (countdown, _, started) = await ScheduleAsync(Hour);

        await countdown.RestartHostAsync(Cancellation);

        Assert.Equal(started, await countdown.Reference.Read());
    }
}
