using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class CountdownRecovery : CountdownTest
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwoHours = TimeSpan.FromHours(2);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    [Fact(DisplayName =
        "Host restart before due still delivers CountdownElapsed exactly once")]
    public async Task RestartBeforeDueDeliversElapsedExactlyOnce()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(OneHour);

        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        await AssertElapsed(destination, started, CountdownResolution.OnTime, cancellationToken);
        await AssertNoFurtherElapsed(test, destination, cancellationToken);
    }

    [Fact(DisplayName =
        "Failed CountdownElapsed commit recovers after host restart")]
    public async Task FailedElapsedCommitRecoversAfterHostRestart()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(OneHour);

        await AssertElapsedCommitFails(countdown, test, destination, cancellationToken);
        Assert.Equal(
            CountdownStatus.Scheduled,
            (await countdown.Reference.Read()).Status);

        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(TimeSpan.Zero, cancellationToken);
        await AssertElapsed(destination, started, CountdownResolution.OnTime, cancellationToken);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);
    }

    [Fact(DisplayName =
        "Failed CountdownElapsed commit recovers without a host restart")]
    public async Task FailedElapsedCommitRecoversWithoutHostRestart()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(OneHour);

        await AssertElapsedCommitFails(countdown, test, destination, cancellationToken);

        await test.Clock.AdvanceAsync(TimeSpan.Zero, cancellationToken);
        await AssertElapsed(destination, started, CountdownResolution.OnTime, cancellationToken);
        await AssertNoFurtherElapsed(test, destination, cancellationToken);
    }

    [Fact(DisplayName =
        "Late delivery beyond one reminder period marks CountdownElapsed as Recovered")]
    public async Task LateDeliveryMarksElapsedAsRecovered()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(OneHour);
        var lateBy = OneMinute + TimeSpan.FromSeconds(1);

        await test.Clock.AdvanceAsync(OneHour + lateBy, cancellationToken);
        var elapsed = await AssertElapsed(
            destination,
            started,
            CountdownResolution.Recovered,
            cancellationToken);

        Assert.Equal(started.DueAt, elapsed.Synapse.DueAt);
        Assert.Equal(started.DueAt + lateBy, elapsed.Synapse.ObservedAt);
        Assert.Equal(test.Clock.UtcNow, elapsed.Synapse.ObservedAt);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);

        await AssertNoFurtherElapsed(test, destination, cancellationToken);
    }

    [Fact(DisplayName =
        "Committed CountdownElapsed survives another host restart without duplication")]
    public async Task CommittedElapsedSurvivesAnotherHostRestartWithoutDuplication()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, _) = await ScheduleAsync(OneHour);

        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        var first = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);

        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(2), cancellationToken);
        var committed = await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken);

        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
    }

    [Fact(DisplayName =
        "Failed Start commit leaves Unscheduled and never delivers CountdownElapsed")]
    public async Task FailedStartCommitLeavesUnscheduledWithoutElapsed()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination) = await PairAsync();
        await using var fault = countdown.FailNextJournalCommit(StartStateCommitFailure);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartAsync(countdown, destination, OneHour));
        Assert.Equal(StartStateCommitFailure, failure.Message);
        Assert.Equal(
            CountdownStatus.Unscheduled,
            (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        await test.Clock.AdvanceAsync(OneMinute, cancellationToken);

        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName =
        "Failed Reschedule commit keeps the committed schedule as authority")]
    public async Task FailedRescheduleCommitKeepsCommittedSchedule()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(TwoHours);
        await using var fault = countdown.FailNextJournalCommit(RescheduleStateCommitFailure);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(CommandId.New(), started.Revision, OneHour)));
        Assert.Equal(RescheduleStateCommitFailure, failure.Message);
        Assert.Equal(started, await countdown.Reference.Read());

        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        await AssertElapsed(destination, started, CountdownResolution.OnTime, cancellationToken);
        await AssertNoFurtherElapsed(test, destination, cancellationToken);
    }

    [Fact(DisplayName =
        "Failed Restart commit keeps Cancelled terminal without CountdownElapsed")]
    public async Task FailedRestartCommitKeepsCancelledWithoutElapsed()
    {
        var cancellationToken = Cancellation;
        var test = await BrainAsync();
        var (countdown, destination, started) = await ScheduleAsync(TwoHours);
        var cancelled = await countdown.Reference.Cancel(new CancelCountdown(CommandId.New(), started.Revision));
        await using var fault = countdown.FailNextJournalCommit(RestartStateCommitFailure);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Restart(new RestartCountdown(CommandId.New(), OneHour)));
        Assert.Equal(RestartStateCommitFailure, failure.Message);
        Assert.Equal(cancelled, await countdown.Reference.Read());

        await test.Clock.AdvanceAsync(OneHour, cancellationToken);
        await test.Clock.AdvanceAsync(OneMinute, cancellationToken);

        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
        Assert.Equal(cancelled, await countdown.Reference.Read());
    }

    private static async Task AssertElapsedCommitFails(
        TestNeuron<ICountdown> countdown,
        TestBrain test,
        TestNeuron<ICountdown> destination,
        CancellationToken cancellationToken)
    {
        await using var fault = countdown.FailNextJournalCommit(OccurrenceCommitFailure);

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(OneHour, cancellationToken));
        Assert.Equal(
            OccurrenceCommitFailure,
            failure.InnerException?.Message);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
    }

    private static async Task<ObservedSynapse<CountdownElapsed>> AssertElapsed(
        TestNeuron<ICountdown> destination,
        CountdownSnapshot started,
        CountdownResolution resolution,
        CancellationToken cancellationToken)
    {
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);
        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(resolution, elapsed.Synapse.Resolution);
        return elapsed;
    }

    private static async Task AssertNoFurtherElapsed(
        TestBrain test,
        TestNeuron<ICountdown> destination,
        CancellationToken cancellationToken)
    {
        await test.Clock.AdvanceAsync(OneMinute, cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(cancellationToken: cancellationToken));
    }
}
