using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Time;
using Orleans.Runtime;
using Reqnroll;
using Xunit;
using ITimer = DigitalBrain.Time.ITimer;

namespace DigitalBrain.Tests;

[Binding]
public sealed class TimeSteps(BrainWorld world)
{
    private Exception? _lastError;
    private Accepted<TimerGeneration>? _lastAccepted;

    [When(@"""(.*)"" schedules timer ""(.*)"" for (\d+) seconds with note ""(.*)""")]
    public async Task Schedule(string principal, string name, int seconds, string note)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        _lastAccepted = await Timer(name).Schedule(new ScheduleTimer(CommandId.New(), seconds, note));
    }

    [When(@"""(.*)"" tries to schedule timer ""(.*)"" for (\d+) seconds with note ""(.*)"" expecting generation (\d+)")]
    public async Task TrySchedule(string principal, string name, int seconds, string note, long expectedGeneration)
    {
        _lastError = null;
        try
        {
            RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
            await Timer(name).Schedule(new ScheduleTimer(CommandId.New(), seconds, note, expectedGeneration));
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }

    [When(@"""(.*)"" stops timer ""(.*)""")]
    public Task Stop(string principal, string name)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        return Timer(name).Stop(new StopTimer(CommandId.New()));
    }

    [Then(@"""(.*)"" waits up to (\d+) seconds until timer ""(.*)"" status is (Unscheduled|Scheduled|Elapsed|Cancelled)")]
    public async Task WaitForStatus(string principal, int seconds, string name, TimerStatus status)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var timer = Timer(name);
        var snapshot = await timer.Read();
        while (snapshot.Status != status && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            snapshot = await timer.Read();
        }

        Assert.Equal(status, snapshot.Status);
    }

    [Then(@"the timer command fails with ""(.*)""")]
    public void CommandFails(string reason)
    {
        Assert.NotNull(_lastError);
        var error = Assert.IsType<CommandRejectedException>(BrainSteps.Flatten(_lastError));
        Assert.Contains(reason, error.Reason, StringComparison.Ordinal);
    }

    [Then(@"timer ""(.*)"" generation is (\d+)")]
    public async Task GenerationIs(string name, long generation)
        => Assert.Equal(generation, (await Timer(name).Read()).Generation);

    [Then(@"timer ""(.*)"" note is ""(.*)""")]
    public async Task NoteIs(string name, string note)
        => Assert.Equal(note, (await Timer(name).Read()).Note);

    [Then(@"the timer command is accepted against generation (\d+)")]
    public void CommandAccepted(long generation)
    {
        Assert.NotNull(_lastAccepted);
        Assert.Equal(new TimerGeneration(generation), _lastAccepted.Receipt);
        Assert.NotEqual(default, _lastAccepted.Work);
    }

    [Then(@"""(.*)"" receives a timer schedule refusal carrying generation (\d+)")]
    public async Task ScheduleRefused(string principal, long generation)
    {
        var session = world.Brain.Grains.GetGrain<INeuron>(NeuronId.Plain(principal).ToGrainId());
        var journal = await session.ReadJournal(JournalKind.Incoming, 0);
        var refusal = Assert.Single(journal.Delta, delivery => delivery.Signal.Type == "TimerScheduleRefused");
        Assert.Equal(new TimerGeneration(generation), refusal.Body(TimeJson.Default.TimerGeneration));
    }

    private ITimer Timer(string name) => world.Brain.Grains.GetGrain<ITimer>(new NeuronId("timer", name).ToGrainId());
}
