using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed partial class CountdownLifecycle
{
    [Fact]
    public async Task CommandsRejectEmptyIdsInvalidDurationsAndDueOverflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("validation");
        var destination = test.Neuron<ICountdown>("destination");
        var empty = default(CommandId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Start(new StartCountdown(
                empty,
                TimeSpan.FromHours(1),
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.Zero,
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromTicks(-1),
                destination.Id)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.MaxValue,
                destination.Id)));

        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                empty,
                started.Revision,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.Zero)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                empty,
                started.Revision)));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                empty,
                TimeSpan.FromHours(1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromTicks(-1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.MaxValue)));

        Assert.Equal(
            cancelled,
            await countdown.Reference.Read());
    }

    [Fact]
    public async Task ReceiptsRetainOnlyTheLatestSixtyFourCommands()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("receipts");
        var destination = test.Neuron<ICountdown>("destination");
        var startCommand = new StartCountdown(
            CommandId.New(),
            TimeSpan.FromHours(1),
            destination.Id);
        var current = await countdown.Reference.Start(startCommand);
        RescheduleCountdown? oldestRetainedCommand = null;
        CountdownSnapshot? oldestRetainedSnapshot = null;

        for (var index = 0; index < 64; index++)
        {
            var command = new RescheduleCountdown(
                CommandId.New(),
                current.Revision,
                TimeSpan.FromHours(1));
            current = await countdown.Reference.Reschedule(command);

            if (index == 0)
            {
                oldestRetainedCommand = command;
                oldestRetainedSnapshot = current;
            }
        }

        Assert.Equal(
            oldestRetainedSnapshot,
            await countdown.Reference.Reschedule(
                Assert.IsType<RescheduleCountdown>(
                    oldestRetainedCommand)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(startCommand));
    }

    [Fact]
    public async Task ReadReturnsCommittedStateAfterHostingSiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("durable-read");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await countdown.RestartHostAsync(cancellationToken);

        Assert.Equal(started, await countdown.Reference.Read());
    }

    [Fact]
    public async Task CountdownEmitsExactlyOnceAtItsDueInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("due");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(59),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(countdown.Id, elapsed.Synapse.Countdown);
        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(destination.Id, elapsed.Synapse.Destination);
        Assert.Equal(started.ScheduledAt, elapsed.Synapse.ScheduledAt);
        Assert.Equal(started.DueAt, elapsed.Synapse.DueAt);
        Assert.Equal(test.Clock.UtcNow, elapsed.Synapse.ObservedAt);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }
}
