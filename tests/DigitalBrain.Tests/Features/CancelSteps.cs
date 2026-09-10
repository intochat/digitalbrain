using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class CancelSteps(BrainSteps brain, BrainWorld world)
{
    [When(@"""[^""]+"" cancels the pending work on ""(.*)""")]
    public Task CancelPending(string name)
    {
        Assert.NotNull(brain.LastFire);
        return brain.Brain.Grains.GetGrain<INeuron>(world.Fixtures[name].ToGrainId())
            .CancelReaction(brain.LastFire.SignalId);
    }

    [Then(@"""(.*)"" reaction observed cancellation")]
    public static Task ThenCancelled(string name)
        => FixtureSwitches.Cancelled[name].Task.WaitAsync(TimeSpan.FromSeconds(10));

    [When(@"""[^""]+"" cancels signal id ""(.*)"" on (\w+) ""(.*)""")]
    public Task CancelId(string handle, string grainType, string name)
        => brain.Brain.Grains.GetGrain<INeuron>(BrainSteps.Id(grainType, name).ToGrainId())
            .CancelReaction(BrainSteps.SignalIdFrom(handle));

    [Then(@"""(.*)"" journal total recorded is (\d+)")]
    public async Task ThenTotalRecorded(string name, long count)
    {
        // A read past the tip carries the snapshot and no delta.
        var read = await brain.Query(name).ReadJournal(JournalKind.Incoming, long.MaxValue);
        Assert.Equal(count, read.ResetSnapshot!.TotalRecorded);
    }
}
