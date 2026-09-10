using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AdmitSteps(BrainSteps brain, BrainWorld world)
{
    private DeliveryAdmission _firstAdmission;
    private DeliveryAdmission _secondAdmission;

    [Given(@"a slow ""(.*)"" whose reaction waits for release")]
    public void GivenSlow(string name)
    {
        world.Fixtures[name] = new NeuronId("slow", name);
        FixtureSwitches.Cancelled[name] = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [Given(@"a throwing ""(.*)"" whose reaction fails (\d+) times then succeeds")]
    public void GivenThrowing(string name, int failures)
    {
        world.Fixtures[name] = new NeuronId("throwing", name);
        FixtureSwitches.ThrowingFailuresLeft[name] = failures;
    }

    [When(@"""(.*)"" fires (\d+) ""(\w+)"" signals at slow ""(.*)""")]
    public async Task FireMany(string from, int count, string type, string name)
    {
        for (var i = 0; i < count; i++)
        {
            await brain.FireCore(from, type, "{}", new NeuronId("slow", name));
        }
    }

    [When(@"""(.*)"" fires ""(\w+)"" at (slow|throwing) ""(.*)""")]
    public Task FireAtTyped(string from, string type, string grainType, string name)
        => brain.FireCore(from, type, "{}", new NeuronId(grainType, name));

    [Then(@"the last fire reports (\d+) busy target")]
    public void ThenBusy(int count)
    {
        Assert.NotNull(brain.LastFire);
        Assert.Equal(count, brain.LastFire.Busy);
    }

    [Then(@"""([^""]*)"" pending count is (\d+)")]
    public async Task ThenPendingCount(string name, int count)
        => Assert.Equal(count, await brain.Brain.Grains.GetGrain<INeuron>(world.Fixtures[name].ToGrainId()).ReadPendingCount());

    [When(@"""(.*)"" waits up to (\d+) seconds until ""(.*)"" pending count is (\d+)")]
    public async Task WaitPendingCount(string observer, int seconds, string name, int count)
    {
        var pending = brain.Brain.Grains.GetGrain<INeuron>(world.Fixtures[name].ToGrainId());
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var observed = await pending.ReadPendingCount();
        while (observed != count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
            observed = await pending.ReadPendingCount();
        }

        if (observed != count)
        {
            Assert.Fail($"{observer} waited {seconds}s for {name} pending count to be {count}, but observed {observed}");
        }
    }

    [When(@"""(.*)"" delivers signal id ""(.*)"" of type ""(\w+)"" to plain ""(.*)"" twice")]
    public async Task DeliverTwice(string from, string handle, string type, string name)
    {
        var delivery = new SignalDelivery(
            Signal.Create(type, "{}"),
            BrainSteps.SignalIdFrom(handle),
            CorrelationId.New(),
            null,
            BrainSteps.Id(from),
            1,
            DateTimeOffset.UtcNow);
        var target = brain.Brain.Grains.GetGrain<INeuron>(NeuronId.Plain(name).ToGrainId());
        _firstAdmission = await target.Deliver(delivery);
        _secondAdmission = await target.Deliver(delivery);
    }

    [Then(@"the second delivery is (\w+)")]
    public void ThenSecondDelivery(string admission)
    {
        Assert.Equal(DeliveryAdmission.Accepted, _firstAdmission);
        Assert.Equal(Enum.Parse<DeliveryAdmission>(admission), _secondAdmission);
    }

    [Then(@"""(.*)"" incoming journal contains (\d+) ""(\w+)""")]
    public async Task ThenIncomingCount(string name, int count, string type)
        => Assert.Equal(count, (await brain.Journal(name, JournalKind.Incoming)).Delta.Count(d => d.Signal.Type == type));
}
