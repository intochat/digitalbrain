using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Tests.Harness;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class AssistantWiringProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task AssistantAlwaysOffersTheCoreSystemTools()
    {
        var brain = fixture.BrainFor("assistant-tools");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");

        await brain.GetGrainProxy<IChat>("main").Send(
            new SendMessage(CommandId.New(), "which tools do you have"));

        var answered = await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text.Length: > 0 } response
                && response.Text != "no tools offered");
        var offered = ((Responded)answered.Synapse).Text;

        Assert.Contains(ValidatedCapability.ToolNameFor("db.connect", 1), offered, StringComparison.Ordinal);
        Assert.Contains(ValidatedCapability.ToolNameFor("db.disconnect", 1), offered, StringComparison.Ordinal);
        Assert.Contains(
            ValidatedCapability.ToolNameFor("introspection.read-topology-request", 1),
            offered,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssistantWiresANaturalLanguageRequest()
    {
        var brain = fixture.BrainFor("assistant-wires");
        var chat = NeuronId.For<IChat>(brain.Owner, "main");
        var feed = NeuronId.For<IProbeSource>(brain.Owner, "elon");
        var chart = NeuronId.For<IChart>(brain.Owner, "dashboard");

        await brain.GetGrainProxy<IChat>("main").Send(
            new SendMessage(CommandId.New(), "connect elon's posts to my dashboard chart"));

        await Graphs.WaitForConnectionTargetAsync(brain, feed, "probe.fact", chart);
        await Journals.WaitForAsync(
            brain, chat, JournalKind.Outgoing,
            delivery => delivery.Synapse is Responded { Text: "wired" });

        await brain.SendAsync<IProbeSource>(
            "elon", new Poke("gm"), TestContext.Current.CancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            var points = await brain.GetGrainProxy<IChart>("dashboard").Read();
            if (points.Any(static point => point is { Series: "posts", Label: "gm" }))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The chart never received the assistant-wired post.");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}
