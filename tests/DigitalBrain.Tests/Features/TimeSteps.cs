using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
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

    [When(@"""(.*)"" schedules timer ""(.*)"" for (\d+) seconds with note ""(.*)""")]
    public Task Schedule(string principal, string name, int seconds, string note)
    {
        RequestContext.Set(NeuronRequestKeys.Caller, NeuronId.Plain(principal).ToString());
        return Timer(name).Schedule(new ScheduleTimer(CommandId.New(), seconds, note));
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

    private ITimer Timer(string name) => world.Brain.Grains.GetGrain<ITimer>(new NeuronId("timer", name).ToGrainId());
}
