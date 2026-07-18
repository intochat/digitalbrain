using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class BddMockChatClientTests
{
    static BddScenario FlightScenario() => new(
        FeatureTitle: "Travel — intent routing",
        ScenarioName: "Find flights",
        PromptPattern: "find.*flight",
        ReplyText: "routing to FindFlightsRequest",
        Tags: Array.Empty<string>(),
        SourceFile: "inline");

    [Fact]
    public async Task Matching_prompt_records_reasoning_against_supplied_neuron_id()
    {
        var probe = new InMemoryReasoningProbe();
        var client = new BddMockChatClient(new[] { FlightScenario() }, probe);

        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [BddMockChatClient.NeuronIdKey] = "FlightSearchNeuron",
            },
        };
        var response = await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "find me a flight to Bali") },
            options,
            TestContext.Current.CancellationToken);

        Assert.Equal("routing to FindFlightsRequest", response.Messages[0].Text);
        Assert.True(probe.TryGet("FlightSearchNeuron", out var hit));
        Assert.Equal("Find flights", hit.ScenarioName);
        Assert.Equal("Travel — intent routing", hit.FeatureTitle);
        Assert.Equal("bdd-mock", hit.Source);
        Assert.Contains("Bali", hit.Prompt);
    }

    [Fact]
    public async Task Unmatched_prompt_throws_BddMockMissException_with_loaded_count()
    {
        var client = new BddMockChatClient(new[] { FlightScenario() }, new InMemoryReasoningProbe());

        var act = () => client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "sing me a song") },
            cancellationToken: TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<BddMockMissException>(act);
        Assert.Equal(1, ex.LoadedScenarios);
        Assert.Equal("sing me a song", ex.UnmatchedPrompt);
    }

    [Fact]
    public async Task Missing_neuron_id_still_records_under_fallback_key()
    {
        var probe = new InMemoryReasoningProbe();
        var client = new BddMockChatClient(new[] { FlightScenario() }, probe);

        await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "find flights to Bali") },
            options: null,
            cancellationToken: TestContext.Current.CancellationToken);

        // The <unattributed> fallback exists so a scenario-match never silently
        // drops — ops can still see "something matched" in the probe.
        Assert.Single(probe.KnownNeurons(), n => n == "<unattributed>");
    }

    [Fact]
    public void Streaming_is_not_supported()
    {
        var client = new BddMockChatClient(new[] { FlightScenario() }, new InMemoryReasoningProbe());
        var act = () => client.GetStreamingResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "find flights") });
        Assert.Throws<NotSupportedException>(act);
    }
}
