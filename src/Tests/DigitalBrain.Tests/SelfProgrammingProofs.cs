using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class SelfProgrammingProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ChatTurnWiresElonsFeedToTheDashboardChart()
    {
        var brain = fixture.BrainFor("act");
        var chat = NeuronId.For<IChat>(brain.Owner, "wiring");
        var planner = new NeuronId("wiringagent", brain.Owner, "planner");
        var feed = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var chart = NeuronId.For<IChart>(brain.Owner, "dashboard");

        await brain.FireAsync<ISynapseGraph>(
            ISynapseGraph.InstanceName,
            new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, planner),
            TestContext.Current.CancellationToken);
        await Graphs.WaitForConnectionTargetAsync(brain, chat, ChatRoles.Responder, planner);

        await brain.GetGrainProxy<IChat>("wiring").Send(
            new SendMessage(CommandId.New(), "please db.connect elon's posts onto my dashboard chart"));

        await Graphs.WaitForConnectionTargetAsync(brain, feed, "probe.fact", chart);
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded answered
                && answered.Text.Contains("wired", StringComparison.Ordinal));

        await brain.FireAsync<IProbeSource>(
            "elon", new Poke("shipped starship"), TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var points = await brain.GetGrainProxy<IChart>("dashboard").Read();
            if (points.Any(static point => point is { Series: "posts", Label: "shipped starship" }))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The chart never received the routed, transformed post.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
