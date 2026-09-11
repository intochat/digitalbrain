using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class AnnounceSteps(BrainSteps brain, BrainWorld world)
{
    private string? _heldQueue;

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

    [Given(@"announcing ""(.*)"" saves twice in its first reaction")]
    public static void SaveFirstReactionTwice(string name) => FixtureSwitches.SaveAnnouncementTwiceOnce[name] = 0;

    [Given(@"session ""(.*)"" pending queue is full")]
    public async Task FillPendingQueue(string name)
    {
        _heldQueue = name;
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
        // Resume the wake-up suppressed by the fixture while the queue was held.
        await brain.Brain.Grains.GetGrain<INeuronInbox>(world.Fixtures[name].ToGrainId()).Drain();
        await ReactionWait.UntilAsync(async () => await brain.Neuron(name).ReadPendingCount() == 0);
    }

    [Then(@"""(.*)"" retains an announcement while ""(.*)"" is full")]
    public async Task RetainedWhileBusy(string source, string receiver)
    {
        var announcing = brain.Brain.Grains.GetGrain<IAnnouncing>(new NeuronId("announcing", source).ToGrainId());
        await ReactionWait.UntilAsync(async () => await announcing.ReadStoredAnnouncements() == 1);
        Assert.Equal(PendingWork.MaxPending, await brain.Neuron(receiver).ReadPendingCount());
        Assert.DoesNotContain((await brain.Journal(receiver, DigitalBrain.Abstractions.Journals.JournalKind.Incoming)).Delta,
            delivery => delivery.Signal.Type == "Pong");
    }

    [AfterScenario]
    public void ReleaseHeldQueue()
    {
        if (_heldQueue is { } name)
        {
            FixtureSwitches.HeldQueues.TryRemove(name, out _);
        }
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
