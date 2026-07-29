using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public sealed class ChatSteps(BrainWorld world)
{
    private const string AssistantName = "assistant";

    [Given("the conversation {string} is observed")]
    public void GivenTheConversationIsObserved(string conversation)
        => _ = world.Neuron<IChat>(conversation);

    [Given("the assistant will reply {string}")]
    public void GivenTheAssistantWillReply(string reply)
        => world.Brain.Chat().Reply(reply);

    [When("the owner sends {string} to the conversation {string}")]
    public Task WhenTheOwnerSends(string text, string conversation)
        => world.Brain.Client.Get<IChat>(conversation).Send(new SendMessage(CommandId.New(), text));

    [Then("the conversation {string} journals the user message {string}")]
    public async Task ThenTheConversationJournalsTheUserMessage(string conversation, string text)
    {
        var messaged = await world.Neuron<IChat>(conversation).Outgoing
            .NextAsync<UserMessaged>(world.CancellationToken);

        Assert.Equal(text, messaged.Synapse.Text);
    }

    [Then("the conversation {string} journals the assistant reply {string}")]
    public async Task ThenTheConversationJournalsTheAssistantReply(string conversation, string reply)
    {
        var responded = await world.Neuron<IChat>(conversation).Outgoing
            .NextAsync<AssistantResponded>(world.CancellationToken);

        Assert.Equal(reply, responded.Synapse.Text);
    }

    [Then("the conversation {string} transcript has {int} turns")]
    public async Task ThenTheTranscriptHasTurns(string conversation, int expected)
    {
        var transcript = await world.Brain.Client.Get<IChat>(conversation).Read();

        Assert.Equal(expected, transcript.Turns.Count);
    }

    [Then("the assistant selects no external capability")]
    public async Task ThenTheAssistantSelectsNoExternalCapability()
    {
        var selected = await world
            .Neuron<IAssistant>(AssistantName)
            .Outgoing
            .ReadAsync<CapabilityToolSelected>(afterSequence: 0, world.CancellationToken);

        Assert.Empty(selected);
    }
}
