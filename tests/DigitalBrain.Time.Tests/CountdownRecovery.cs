using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Time.Tests;

public sealed class CountdownRecovery(TimeFixture fixture)
{
    [Fact]
    public async Task RestartBeforeDueRecoversExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("restart-before-due");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task FailedOccurrenceCommitRecoversAfterRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("occurrence-fault");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));
        await using var fault = countdown.FailNextJournalCommit(
            "countdown occurrence commit failure");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(
                TimeSpan.FromHours(1),
                cancellationToken));
        Assert.Equal(
            "countdown occurrence commit failure",
            failure.InnerException?.Message);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        Assert.Equal(
            CountdownStatus.Scheduled,
            (await countdown.Reference.Read()).Status);
        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.Zero,
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);
    }

    [Fact]
    public async Task FailedOccurrenceCommitRecoversWithoutAHostRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("occurrence-fault-no-restart");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));
        await using var fault = countdown.FailNextJournalCommit(
            "countdown occurrence commit failure");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(
                TimeSpan.FromHours(1),
                cancellationToken));
        Assert.Equal(
            "countdown occurrence commit failure",
            failure.InnerException?.Message);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.Zero,
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact(DisplayName = "Late delivery beyond one reminder period marks CountdownElapsed as Recovered")]
    public async Task LateDeliveryBeyondOneReminderPeriodMarksRecovered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("recovered-late");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));
        var lateBy = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1) + lateBy,
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(started.DueAt, elapsed.Synapse.DueAt);
        Assert.Equal(started.DueAt + lateBy, elapsed.Synapse.ObservedAt);
        Assert.Equal(test.Clock.UtcNow, elapsed.Synapse.ObservedAt);
        Assert.Equal(CountdownResolution.Recovered, elapsed.Synapse.Resolution);
        Assert.Equal(
            CountdownStatus.Elapsed,
            (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task CommittedOccurrenceSurvivesAnotherRestartWithoutDuplication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("committed");
        var destination = test.Neuron<ICountdown>("destination");
        _ = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(1));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        var first = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        await countdown.RestartHostAsync(cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(2),
            cancellationToken);
        var committed = await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken);

        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
    }

    [Fact]
    public async Task StateLessOrphanReminderSelfRetiresWithoutEmission()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("state-less-orphan");
        var destination = test.Neuron<ICountdown>("destination");
        await using var fault = countdown.FailNextJournalCommit(
            "start state commit failure");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Start(new StartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1),
                destination.Id)));
        Assert.Equal("start state commit failure", failure.Message);
        Assert.Equal(
            CountdownStatus.Unscheduled,
            (await countdown.Reference.Read()).Status);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);

        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task RevisionMismatchedOrphanCannotReplaceTheCommittedSchedule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("revision-orphan");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(2));
        await using var fault = countdown.FailNextJournalCommit(
            "reschedule state commit failure");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Reschedule(new RescheduleCountdown(
                CommandId.New(),
                started.Revision,
                TimeSpan.FromHours(1))));
        Assert.Equal("reschedule state commit failure", failure.Message);
        Assert.Equal(started, await countdown.Reference.Read());

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(
            cancellationToken);

        Assert.Equal(started.Generation, elapsed.Synapse.Generation);
        Assert.Equal(started.Revision, elapsed.Synapse.Revision);
        Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);

        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);
        Assert.Single(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
    }

    [Fact]
    public async Task GenerationMismatchedOrphanCannotRestartACancelledCountdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var countdown = test.Neuron<ICountdown>("generation-orphan");
        var destination = test.Neuron<ICountdown>("destination");
        var started = await Start(
            countdown,
            destination,
            TimeSpan.FromHours(2));
        var cancelled = await countdown.Reference.Cancel(
            new CancelCountdown(
                CommandId.New(),
                started.Revision));
        await using var fault = countdown.FailNextJournalCommit(
            "restart state commit failure");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => countdown.Reference.Restart(new RestartCountdown(
                CommandId.New(),
                TimeSpan.FromHours(1))));
        Assert.Equal("restart state commit failure", failure.Message);
        Assert.Equal(cancelled, await countdown.Reference.Read());

        await test.Clock.AdvanceAsync(
            TimeSpan.FromHours(1),
            cancellationToken);
        await test.Clock.AdvanceAsync(
            TimeSpan.FromMinutes(1),
            cancellationToken);

        Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(
            cancellationToken: cancellationToken));
        Assert.Equal(cancelled, await countdown.Reference.Read());
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
