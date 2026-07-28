using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.Chat.Tests;

public sealed class StreamingNeuronGuards
{
    [Fact(DisplayName = "a neuron contract returning IAsyncEnumerable does not trip RequireSerializedTurns")]
    public void AsyncEnumerableContractSurvivesSerializedTurnGuard()
    {
        var guard = typeof(Neuron).Assembly.GetType("DigitalBrain.Kernel.NeuronConcurrency")!;
        var require = guard.GetMethod(
            "RequireSerializedTurns",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;

        var invoke = Record.Exception(() => require.Invoke(null, [typeof(ChatNeuronProbe)]));

        Assert.Null(invoke?.InnerException);
    }

    [Fact(DisplayName = "ChatResponseUpdate round-trips the Orleans STJ codec with $type intact for text and data")]
    public void ChatResponseUpdateRoundTripsWithPolymorphicContent()
    {
        var services = new ServiceCollection()
            .AddSerializer(serializer => serializer.AddJsonSerializer(
                static type => type == typeof(ChatResponseUpdate)
                    || typeof(AIContent).IsAssignableFrom(type)))
            .BuildServiceProvider();

        var codec = services.GetRequiredService<Serializer<ChatResponseUpdate>>();
        var original = new ChatResponseUpdate(ChatRole.Assistant, "hi")
        {
            Contents =
            [
                new TextContent("hi"),
                new DataContent("data:image/png;base64,iVBORw0KGgo=", "image/png"),
            ],
        };

        var revived = codec.Deserialize(codec.SerializeToArray(original));

        Assert.Collection(
            revived.Contents,
            content => Assert.IsType<TextContent>(content),
            content => Assert.Equal("image/png", Assert.IsType<DataContent>(content).MediaType));
    }
}

internal sealed class ChatNeuronProbe : Neuron
{
    public IAsyncEnumerable<ChatResponseUpdate> Probe(CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}
