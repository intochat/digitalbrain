using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace DigitalBrain.ModuleTests;

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
        var codec = WireCodec();
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

    [Fact(DisplayName = "ChatResponseUpdate round-trips reasoning content through the client wire serializers")]
    public void ChatResponseUpdateRoundTripsReasoningContent()
    {
        var codec = WireCodec();
        var original = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextReasoningContent("thinking out loud"), new TextContent("hello")],
        };

        var revived = codec.Deserialize(codec.SerializeToArray(original));

        Assert.Collection(
            revived.Contents,
            content => Assert.Equal("thinking out loud", Assert.IsType<TextReasoningContent>(content).Text),
            content => Assert.Equal("hello", Assert.IsType<TextContent>(content).Text));
    }

    private static Serializer<ChatResponseUpdate> WireCodec()
        => new ServiceCollection()
            .AddSerializer(serializer => serializer.AddJsonSerializer(
                static type => type == typeof(ChatMessage)
                    || type == typeof(ChatResponse)
                    || type == typeof(ChatResponseUpdate)
                    || typeof(AIContent).IsAssignableFrom(type)))
            .BuildServiceProvider()
            .GetRequiredService<Serializer<ChatResponseUpdate>>();
}

internal abstract class ChatNeuronProbe : Neuron
{
    public IAsyncEnumerable<ChatResponseUpdate> Probe(CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<ChatResponseUpdate>();
}
