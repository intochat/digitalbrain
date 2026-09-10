using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AnnounceSteps(BrainSteps brain, BrainWorld world)
{
    [Given(@"""(.*)"" is connected from announcing ""(.*)"" for ""(\w+)""")]
    public Task ConnectReceiver(string receiver, string source, string type)
    {
        // The receiver is a holding neuron so a scenario can fill its pending queue.
        world.Fixtures[receiver] = new NeuronId("holding", receiver);
        return brain.Neuron(new NeuronId("announcing", source).ToString()).Connect(world.Fixtures[receiver], type);
    }

    [Given(@"announcing ""(.*)"" is connected to plain ""(.*)"" for ""(\w+)""")]
    public Task ConnectPlain(string source, string target, string type)
        => brain.Neuron(new NeuronId("announcing", source).ToString()).Connect(NeuronId.Plain(target), type);

    [Given(@"announcing ""(.*)"" loses its activation after the next snapshot save")]
    public static void LoseActivationAfterSnapshotSave(string name)
        => FixtureReactionCrashPoint.LoseActivationOnce[new NeuronId("announcing", name).ToString()] = 0;

    [Given(@"delivery to ""(.*)"" fails once")]
    public static void FailDeliveryOnce(string name) => FixtureSwitches.DeliveryFailuresLeft[name] = 1;

    [Given(@"announcing ""(.*)"" forgets to save its first reaction")]
    public static void ForgetFirstReactionSave(string name) => FixtureSwitches.ForgetAnnouncementSaveOnce[name] = 0;

    [Given(@"session ""(.*)"" pending queue is full")]
    public async Task FillPendingQueue(string name)
    {
        FixtureSwitches.HeldQueues[name] = 0;
        for (var i = 0; i < PendingWork.MaxPending; i++)
        {
            await brain.FireCore("filler", "Tick", "{}", world.Fixtures[name]);
            Assert.Null(brain.LastError);
        }

        Assert.Equal(PendingWork.MaxPending, await brain.Neuron(name).ReadPendingCount());
    }

    [When(@"session ""(.*)"" pending queue drains")]
    public async Task DrainPendingQueue(string name)
    {
        FixtureSwitches.HeldQueues.TryRemove(name, out _);
        // Repeated failures backed the retry timer off to a minute, and Deliver cannot wake a full queue, so kick the drain instead of waiting for the timer.
        await brain.Brain.Grains.GetGrain<INeuronInbox>(world.Fixtures[name].ToGrainId()).Drain();
        await ReactionWait.UntilAsync(async () => await brain.Neuron(name).ReadPendingCount() == 0);
    }

    [Then(@"""(.*)"" tally is (\d+)")]
    public async Task ThenTally(string name, int tally)
        => Assert.Equal(tally, await brain.Brain.Grains
            .GetGrain<IAnnouncing>(new NeuronId("announcing", name).ToGrainId()).ReadTally());

    [Then(@"""(.*)"" has no stored announcements")]
    public Task ThenNoStoredAnnouncements(string name)
    {
        var announcing = brain.Brain.Grains.GetGrain<IAnnouncing>(new NeuronId("announcing", name).ToGrainId());
        return ReactionWait.UntilAsync(async () => await announcing.ReadStoredAnnouncements() == 0);
    }
}
