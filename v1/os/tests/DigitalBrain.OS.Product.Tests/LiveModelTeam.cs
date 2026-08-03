using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveModelTeam
{
    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: convene_model_team runs Gemma4 and Llama32 in one team and each model's GenAI span carries usage")]
    public async Task ModelTeamIsObservable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = LiveProductAspire.FindRepositoryRoot();
        var chatName = $"team-verification-{Guid.NewGuid():N}";
        var commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        const string prompt =
            "Compare Gemma4 and Llama32 directly: convene them as one team and ask them both, "
            + "in one word, what color is a clear daytime sky? Then report what each answered.";

        await LiveProductAspire.RunScenarioAsync(
            repository,
            ["silo", LiveProductAspire.McpResource, "brain-ai-gemma4", "brain-ai-llama32"],
            async () =>
            {
                var result = await LiveProductAspire.CallToolAsync(
                    repository,
                    "send_chat_message",
                    JsonSerializer.Serialize(
                        new
                        {
                            text = prompt,
                            commandId,
                            chatName,
                            timeoutSeconds = 300,
                        }),
                    cancellationToken);
                var response = LiveProductJson.RequiredString(result, "response");

                Assert.False(string.IsNullOrWhiteSpace(response));

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
                        "UserMessaged",
                        StringComparison.Ordinal));
                Assert.Contains(
                    entries,
                    entry => string.Equals(
                        LiveProductJson.RequiredString(entry, "synapse"),
                        "AssistantResponded",
                        StringComparison.Ordinal));
                Assert.Contains(
                    entries,
                    entry => string.Equals(
                        LiveProductJson.RequiredString(entry, "synapse"),
                        "CapabilityRequested",
                        StringComparison.Ordinal));

                var llama = await LiveProductAspire.WaitForGenAiSpanAsync(
                    repository,
                    OllamaChatSpanFor("llama3.2"),
                    "llama3.2 participant chat span",
                    cancellationToken);
                var gemma = await LiveProductAspire.WaitForGenAiSpanAsync(
                    repository,
                    OllamaChatSpanFor("gemma4"),
                    "gemma4 participant chat span",
                    cancellationToken);

                AssertTokenUsage(llama);
                AssertTokenUsage(gemma);
            },
            cancellationToken);
    }

    private static Func<JsonObject, bool> OllamaChatSpanFor(string model)
        => candidate => candidate["attributes"] is JsonObject attributes
            && string.Equals(
                LiveProductJson.OptionalString(attributes, "gen_ai.operation.name"),
                "chat",
                StringComparison.Ordinal)
            && string.Equals(
                LiveProductJson.OptionalString(attributes, "gen_ai.provider.name"),
                "ollama",
                StringComparison.Ordinal)
            && LiveProductJson.OptionalString(attributes, "gen_ai.request.model")
                ?.Contains(model, StringComparison.OrdinalIgnoreCase) is true;

    private static void AssertTokenUsage(JsonObject span)
    {
        var attributes = LiveProductJson.RequiredObject(span, "attributes");

        Assert.True(LiveProductJson.RequiredLong(attributes, "gen_ai.usage.input_tokens") > 0);
        Assert.True(LiveProductJson.RequiredLong(attributes, "gen_ai.usage.output_tokens") > 0);
    }
}
