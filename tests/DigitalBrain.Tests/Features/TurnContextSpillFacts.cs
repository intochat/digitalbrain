using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TurnContextSpillFacts
{
    [Fact]
    public async Task AgedContextPayloadRoundTripsThroughBlobStore()
    {
        const string payload = "{ \"name\": \"Žofie\" }\n";
        var store = new MemoryTurnContextStore();
        List<TurnContext> contexts = [];
        for (var index = 0; index <= ChatState.MaxInlineContexts; index++)
        {
            contexts.Add(TurnContexts.For(SignalId.New(), "hello",
                [new ContextRef("customer.name", "text.v1", payload)], contexts));
        }

        var original = contexts[0].Slots[0];
        await TurnContexts.RetainAsync(contexts, store, CancellationToken.None);

        var spilled = contexts[0].Slots[0];
        Assert.Null(spilled.PayloadJson);
        Assert.Equal(original.Digest, spilled.Digest);
        Assert.NotNull(spilled.BlobRef);
        Assert.Equal(payload, await store.ReadAsync(spilled.BlobRef, CancellationToken.None));
    }
}
