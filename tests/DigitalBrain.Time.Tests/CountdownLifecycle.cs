using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed partial class CountdownLifecycle(TimeFixture fixture)
{
    [Fact]
    public async Task StartIsIdempotentAndAllowedOnlyFromUnscheduled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("start");
        var destination = test.Neuron<ICountdown>("destination");
        var empty = await countdown.Reference.Read();

        Assert.Equal(CountdownStatus.Unscheduled, empty.Status);
        Assert.Equal(0, empty.Generation);
        Assert.Equal(0, empty.Revision);
        Assert.Null(empty.Destination);

        var command = new StartCountdown(
            CommandId.New(),
            TimeSpan.FromHours(1),
            destination.Id);
        var started = await countdown.Reference.Start(command);
        var repeated = await countdown.Reference.Start(command);

        Assert.Equal(started, repeated);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(1, started.Generation);
        Assert.Equal(1, started.Revision);
        Assert.Equal(destination.Id, started.Destination);
        Assert.Equal(test.Clock.UtcNow, started.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(1), started.DueAt);
        Assert.Equal(TimeSpan.FromHours(1), started.Duration);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                destination.Id)));
    }

    [Fact]
    public async Task RescheduleUsesTheExactRevisionAndInvalidatesThePriorWakeup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("reschedule");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                ExpectedRevision: started.Revision + 1,
                TimeSpan.FromHours(1))));

        var rescheduled = await countdown.Reference.Reschedule(
            new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.FromHours(1)));

        Assert.Equal(CountdownStatus.Scheduled, rescheduled.Status);
        Assert.Equal(started.Generation, rescheduled.Generation);
        Assert.Equal(started.Revision + 1, rescheduled.Revision);
        Assert.Equal(started.Destination, rescheduled.Destination);
        Assert.Equal(test.Clock.UtcNow, rescheduled.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(1), rescheduled.DueAt);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(30),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(rescheduled.Generation, elapsed.Synapse.Generation);
        Assert.Equal(rescheduled.Revision, elapsed.Synapse.Revision);
    }

    [Fact]
    public async Task CancelUsesTheExactRevisionAndIsTerminal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("cancel");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                CommandId.New(),
                ExpectedRevision: started.Revision + 1)));

        var command = new CancelCountdown(
            CommandId.New(),
            started.Revision);
        var cancelled = await countdown.Reference.Cancel(command);
        var repeated = await countdown.Reference.Cancel(command);

        Assert.Equal(cancelled, repeated);
        Assert.Equal(CountdownStatus.Cancelled, cancelled.Status);
        Assert.Equal(started.Generation, cancelled.Generation);
        Assert.Equal(started.Revision, cancelled.Revision);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                CommandId.New(),
                cancelled.Revision)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                cancelled.Revision,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                destination.Id)));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(2),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RestartRetainsDestinationAndStartsANewGeneration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("restart");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1))));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));
        var restarted = await countdown.Reference.Restart(
            new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(2)));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(cancelled.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(cancelled.Destination, restarted.Destination);
        Assert.Equal(test.Clock.UtcNow, restarted.ScheduledAt);
        Assert.Equal(test.Clock.UtcNow + TimeSpan.FromHours(2), restarted.DueAt);
        Assert.Equal(TimeSpan.FromHours(2), restarted.Duration);
    }

    [Fact]
    public async Task RestartIsAllowedAfterElapsed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("restart-elapsed");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        _ = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        var restarted = await countdown.Reference.Restart(
            new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(2)));

        Assert.Equal(CountdownStatus.Scheduled, restarted.Status);
        Assert.Equal(started.Generation + 1, restarted.Generation);
        Assert.Equal(1, restarted.Revision);
        Assert.Equal(started.Destination, restarted.Destination);
    }

    [Fact]
    public async Task DestinationMustBelongToTheCountdownOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("owner");
        var foreign = test.Owner("foreign").Neuron<ICountdown>("destination");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                foreign.Id)));

        Assert.Equal(
            CountdownStatus.Unscheduled,
            (await countdown.Reference.Read()).Status);
    }

    private static Task<CountdownSnapshot> Start(
        TestNeuron<ICountdown> countdown,
        TestNeuron<ICountdown> destination,
        TimeSpan duration)
        => countdown.Reference.Start(new StartCountdown(
            CommandId.New(),
            duration,
            destination.Id));
}
