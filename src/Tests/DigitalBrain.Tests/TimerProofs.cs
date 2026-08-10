using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using DigitalBrain.Time;
using Xunit;
using ITimer = DigitalBrain.Time.ITimer;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class TimerProofs(BrainClusterFixture fixture)
{
    private static readonly TimeSpan ElapsePatience = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task StartArmsTheTimerAndReportsTheDueInstant()
    {
        var brain = fixture.BrainFor("timer-start");

        var scheduled = await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 300, "tea in five"),
            TestContext.Current.CancellationToken);

        Assert.Equal("tea in five", scheduled.Note);
        Assert.Equal(scheduled.ScheduledAt + TimeSpan.FromSeconds(300), scheduled.DueAt);
        Assert.Equal(1, scheduled.Generation);

        var snapshot = await brain.GetGrainProxy<ITimer>().Read();
        Assert.Equal(TimerStatus.Scheduled, snapshot.Status);
        Assert.Equal(scheduled.DueAt, snapshot.DueAt);
    }

    [Fact]
    public async Task StartWhileScheduledRefusesAndKeepsTheOriginalSchedule()
    {
        var brain = fixture.BrainFor("timer-refuse");
        var timer = NeuronId.For<ITimer>(brain.Owner, "default");

        var original = await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 600, "original"),
            TestContext.Current.CancellationToken);

        await brain.FireAsync(
            timer,
            new StartTimer(CommandId.New(), DurationSeconds: 60, "usurper"),
            TestContext.Current.CancellationToken);
        await Journals.WaitForAsync(
            brain, timer, JournalKind.Incoming,
            delivery => delivery.Synapse is StartTimer { Note: "usurper" });

        var snapshot = await brain.GetGrainProxy<ITimer>().Read();
        Assert.Equal(TimerStatus.Scheduled, snapshot.Status);
        Assert.Equal(original.DueAt, snapshot.DueAt);
        Assert.Equal("original", snapshot.Note);
    }

    [Fact]
    public async Task CancelStopsTheScheduledTimer()
    {
        var brain = fixture.BrainFor("timer-cancel");

        await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 600, "long wait"),
            TestContext.Current.CancellationToken);

        var cancelled = await brain.Get<ITimer>().FireAsync(
            new CancelTimer(CommandId.New()),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, cancelled.Generation);
        Assert.Equal(TimerStatus.Cancelled, (await brain.GetGrainProxy<ITimer>().Read()).Status);
    }

    [Fact]
    public async Task ScheduledTimerPostsAClockCardIntoChatThroughTheGraph()
    {
        var brain = fixture.BrainFor("timer-card");
        var timer = NeuronId.For<ITimer>(brain.Owner, "default");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(
                Guid.NewGuid(),
                timer,
                "time.timer-scheduled",
                chat,
                "to:ui.timer-card{Label=Note,DueAt=DueAt}"),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, timer, "time.timer-scheduled");

        await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 300, "tea in five"),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Timers.Length: > 0 } posted
                && posted.Timers[0].Label == "tea in five");

        var transcript = await brain.GetGrainProxy<IChat>("main").Read();
        Assert.Contains(
            transcript.Turns,
            turn => turn.Timers is { Length: > 0 } offers && offers[0].Label == "tea in five");
    }

    [Fact]
    public async Task ElapsedTimerPostsItsNoteIntoChatThroughTheGraph()
    {
        var brain = fixture.BrainFor("timer-elapse");
        var timer = NeuronId.For<ITimer>(brain.Owner, "default");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(
                Guid.NewGuid(),
                timer,
                "time.timer-elapsed",
                chat,
                "to:ui.note{Text=Note}"),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionsAsync(brain, timer, "time.timer-elapsed");

        await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 1, "the tea is ready"),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "the tea is ready" },
            ElapsePatience);

        Assert.Equal(TimerStatus.Elapsed, (await brain.GetGrainProxy<ITimer>().Read()).Status);
    }

    [Fact]
    public async Task StartAfterElapseArmsANewGeneration()
    {
        var brain = fixture.BrainFor("timer-regenerate");

        await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 1, "first sitting"),
            TestContext.Current.CancellationToken);
        await WaitForStatusAsync(brain, TimerStatus.Elapsed);

        var second = await brain.Get<ITimer>().FireAsync(
            new StartTimer(CommandId.New(), DurationSeconds: 300, "second sitting"),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, second.Generation);
        Assert.Equal("second sitting", (await brain.GetGrainProxy<ITimer>().Read()).Note);
    }

    private static async Task WaitForStatusAsync(Client.IDigitalBrain brain, TimerStatus status)
    {
        var deadline = DateTime.UtcNow + ElapsePatience;

        while (DateTime.UtcNow < deadline)
        {
            if ((await brain.GetGrainProxy<ITimer>().Read()).Status == status)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        throw new TimeoutException($"The timer never reached {status} within {ElapsePatience}.");
    }
}

public sealed class TimerCapabilityManifestProofs
{
    [Fact]
    public void TimerVerbsAreDeclaredCapabilities()
    {
        var manifest = DigitalBrain.Core.ModuleReflection.ManifestOf(typeof(StartTimer).Assembly);

        var timer = Assert.Single(
            manifest.Neurons,
            neuron => neuron.ContractId == "timer");

        Assert.Contains(timer.Accepted, synapse => synapse.ContractId == "time.start-timer");
        Assert.Contains(timer.Accepted, synapse => synapse.ContractId == "time.cancel-timer");
        Assert.Contains(timer.Emitted, synapse => synapse.ContractId == "time.timer-scheduled");
        Assert.Contains(timer.Emitted, synapse => synapse.ContractId == "time.timer-cancelled");
        Assert.Contains(manifest.Facts, fact => fact.ContractId == "time.timer-elapsed");
    }
}
