using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AnnouncementRefireFacts
{
    [Fact]
    public async Task RefiredAnnouncementRetainsExactlyOneOutgoingEntry()
    {
        await using var simulation = await BrainSteps.StartSimulationAsync();
        FixtureSwitches.DeliveryFailuresLeft["p"] = 1;
        var announcingId = new NeuronId("announcing", "a");
        var announcing = simulation.Grains.GetGrain<INeuron>(announcingId.ToGrainId());
        var receiver = simulation.Grains.GetGrain<INeuron>(NeuronId.Plain("p").ToGrainId());
        var sender = simulation.Grains.GetGrain<INeuron>(NeuronId.Plain("claude").ToGrainId());
        await announcing.Connect(NeuronId.Plain("p"), "Pong");

        await sender.Fire(Signal.Create("Ping", "{}"), announcingId, null, TestContext.Current.CancellationToken);
        await ReactionWait.UntilAsync(async () =>
            (await receiver.ReadJournal(JournalKind.Incoming, 0)).Delta.Any(entry => entry.Signal.Type == "Pong"),
            TestContext.Current.CancellationToken);

        var outgoing = await announcing.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Single(outgoing.Delta, entry => entry.Signal.Type == "Pong");
    }
}
