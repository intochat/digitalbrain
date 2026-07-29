using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveProduct
{
    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: real Gemma chat is idempotent, journaled, owner-scoped and emits content-rich GenAI telemetry in Development")]
    public async Task RealChatIsObservable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = LiveProductAspire.FindRepositoryRoot();
        var chatName = $"product-verification-{Guid.NewGuid():N}";
        var commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        const string prompt = "Tell me your name in one short sentence.";

        await LiveProductAspire.RunScenarioAsync(
            repository,
            ["silo", LiveProductAspire.McpResource, "brain-ai-gemma4"],
            async () =>
            {
                var sendInput = JsonSerializer.Serialize(
                    new
                    {
                        text = prompt,
                        commandId,
                        chatName,
                        timeoutSeconds = 300,
                    });
                var result = await LiveProductAspire.CallToolAsync(
                    repository,
                    "send_chat_message",
                    sendInput,
                    cancellationToken);
                var response = LiveProductJson.RequiredString(result, "response");
                var correlationId = LiveProductJson.RequiredString(result, "correlationId");

                Assert.False(string.IsNullOrWhiteSpace(response));
                Assert.True(Guid.TryParse(LiveProductJson.RequiredString(result, "commandId"), out _));
                Assert.True(Guid.TryParse(correlationId, out _));

                var retried = await LiveProductAspire.CallToolAsync(
                    repository,
                    "send_chat_message",
                    sendInput,
                    cancellationToken);
                Assert.Equal(response, LiveProductJson.RequiredString(retried, "response"));
                Assert.Equal(correlationId, LiveProductJson.RequiredString(retried, "correlationId"));

                var transcript = await LiveProductAspire.CallToolAsync(
                    repository,
                    "read_chat_transcript",
                    JsonSerializer.Serialize(new { chatName }),
                    cancellationToken);
                var turns = LiveProductJson.RequiredArray(transcript, "turns");
                Assert.Collection(
                    turns,
                    turn =>
                    {
                        Assert.Equal("you", LiveProductJson.RequiredString(turn, "speaker"));
                        Assert.Equal(prompt, LiveProductJson.RequiredString(turn, "text"));
                    },
                    turn =>
                    {
                        Assert.Equal("brain", LiveProductJson.RequiredString(turn, "speaker"));
                        Assert.Equal(response, LiveProductJson.RequiredString(turn, "text"));
                    });

                var journal = await LiveProductAspire.CallToolAsync(
                    repository,
                    "read_neuron_journal",
                    JsonSerializer.Serialize(
                        new
                        {
                            grainType = "chat",
                            name = chatName,
                            kind = "outgoing",
                            afterSequence = 0,
                        }),
                    cancellationToken);
                var entries = LiveProductJson.RequiredArray(journal, "entries");
                Assert.Contains(
                    entries,
                    entry => string.Equals(
                        LiveProductJson.RequiredString(entry, "synapse"),
                        "CapabilityRequested",
                        StringComparison.Ordinal));
                Assert.Contains(
                    entries,
                    entry => string.Equals(
                        LiveProductJson.RequiredString(entry, "synapse"),
                        "CapabilityCompleted",
                        StringComparison.Ordinal)
                        || string.Equals(
                            LiveProductJson.RequiredString(entry, "synapse"),
                            "CapabilityAbandoned",
                            StringComparison.Ordinal));

                var chatFacts = entries
                    .Where(entry => LiveProductJson.RequiredString(entry, "synapse") is "UserMessaged" or "AssistantResponded")
                    .ToArray();
                Assert.Collection(
                    chatFacts,
                    entry =>
                    {
                        Assert.Equal("UserMessaged", LiveProductJson.RequiredString(entry, "synapse"));
                        Assert.Equal(correlationId, LiveProductJson.RequiredString(entry, "correlation"));
                    },
                    entry =>
                    {
                        Assert.Equal("AssistantResponded", LiveProductJson.RequiredString(entry, "synapse"));
                        Assert.Equal(correlationId, LiveProductJson.RequiredString(entry, "correlation"));
                    });

                var activeNeurons = await LiveProductAspire.CallToolAsync(
                    repository,
                    "list_active_neurons",
                    "{}",
                    cancellationToken);
                var activeChat = activeNeurons
                    .AsArray()
                    .Single(neuron =>
                        string.Equals(LiveProductJson.RequiredString(neuron, "grainType"), "chat", StringComparison.Ordinal)
                        && string.Equals(
                            LiveProductJson.RequiredString(neuron, "identity"),
                            $"dev/{chatName}",
                            StringComparison.Ordinal));
                Assert.NotNull(activeChat);
                Assert.DoesNotContain(
                    "\"silo\"",
                    activeNeurons.ToJsonString(),
                    StringComparison.OrdinalIgnoreCase);

                var span = await LiveProductAspire.WaitForGenAiSpanAsync(
                    repository,
                    candidate => candidate["attributes"] is JsonObject candidateAttributes
                        && string.Equals(
                            LiveProductJson.OptionalString(candidateAttributes, "gen_ai.operation.name"),
                            "chat",
                            StringComparison.Ordinal)
                        && string.Equals(
                            LiveProductJson.OptionalString(candidateAttributes, "gen_ai.provider.name"),
                            "ollama",
                            StringComparison.Ordinal)
                        && LiveProductJson.OptionalString(candidateAttributes, "gen_ai.input.messages")
                            ?.Contains(prompt, StringComparison.Ordinal) is true,
                    "content-rich Ollama chat span carrying the prompt",
                    cancellationToken);
                var attributes = LiveProductJson.RequiredObject(span, "attributes");

                Assert.Equal("chat", LiveProductJson.RequiredString(attributes, "gen_ai.operation.name"));
                Assert.Equal("ollama", LiveProductJson.RequiredString(attributes, "gen_ai.provider.name"));
                Assert.Contains(
                    "gemma4",
                    LiveProductJson.RequiredString(attributes, "gen_ai.request.model"),
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(LiveProductJson.RequiredLong(attributes, "gen_ai.usage.input_tokens") > 0);
                Assert.True(LiveProductJson.RequiredLong(attributes, "gen_ai.usage.output_tokens") > 0);
                Assert.False(
                    string.IsNullOrWhiteSpace(
                        LiveProductJson.RequiredString(attributes, "gen_ai.response.finish_reasons")));
                Assert.Contains(
                    prompt,
                    LiveProductJson.RequiredString(attributes, "gen_ai.input.messages"),
                    StringComparison.Ordinal);
                Assert.Contains(
                    response,
                    LiveProductJson.RequiredString(attributes, "gen_ai.output.messages"),
                    StringComparison.Ordinal);
            },
            cancellationToken);
    }
}
