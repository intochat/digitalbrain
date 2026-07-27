using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed partial class CountdownLifecycle
{
    [Fact(DisplayName =
        "Empty CommandId is rejected on Start, Reschedule, Cancel, and Restart")]
    public async Task EmptyCommandIdIsRejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var (countdown, destination) = TimeFixture.Pair(test);
        var empty = default(CommandId);

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Start(new StartCountdown(
                empty,
                Hour,
                destination.Id)));

        var started = await TimeFixture.Start(countdown, destination, Hour);

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                empty,
                started.Revision,
                Hour)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Cancel(new CancelCountdown(
                empty,
                started.Revision)));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));

        await Assert.ThrowsAsync<ArgumentException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                empty,
                Hour)));

        Assert.Equal(cancelled, await countdown.Reference.Read());
    }

    [Fact(DisplayName =
        "Duration must be positive and yield a due instant within the supported range")]
    public async Task DurationMustBePositiveAndWithinRange()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var (countdown, destination) = TimeFixture.Pair(test);

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

        var started = await TimeFixture.Start(countdown, destination, Hour);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.Zero)));

        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(CommandId.New(), started.Revision));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromTicks(-1))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.MaxValue)));

        Assert.Equal(cancelled, await countdown.Reference.Read());
    }

    [Fact(DisplayName =
        "Destination must belong to the same owner as the countdown")]
    public async Task DestinationMustBelongToTheCountdownOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var (countdown, _) = TimeFixture.Pair(test);
        var foreign = test.Owner("foreign").Neuron<ICountdown>(TimeFixture.Destination);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                Hour,
                foreign.Id)));

        Assert.Equal(
            CountdownStatus.Unscheduled,
            (await countdown.Reference.Read()).Status);
    }
}
