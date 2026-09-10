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

    [When(@"""[^""]+"" cancels signal id ""(.*)"" on plain ""(.*)""")]
    public Task CancelId(string handle, string name)
        => brain.Brain.Grains.GetGrain<INeuron>(NeuronId.Plain(name).ToGrainId())
            .CancelReaction(BrainSteps.SignalIdFrom(handle));

    [Then(@"""(.*)"" journal total recorded is (\d+)")]
    public async Task ThenTotalRecorded(string name, long count)
    {
        var query = brain.Query(name);
        var read = await query.ReadJournal(JournalKind.Incoming, 0);
        var past = await query.ReadJournal(JournalKind.Incoming, read.ResumeSequence + 1);
        Assert.Equal(count, past.ResetSnapshot?.TotalRecorded ?? read.ResetSnapshot?.TotalRecorded ?? read.Delta.Count);
    }
}
