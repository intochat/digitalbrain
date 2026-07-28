using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

[Alias("DigitalBrain.ModuleTests.IStreamingRelayProbe")]
[ClientEntryPoint]
public partial interface IStreamingRelayProbe : INeuron
{
    [Alias(nameof(CollectStreamingUpdates))]
    Task<IReadOnlyList<ChatResponseUpdate>> CollectStreamingUpdates(string modelName, string prompt);

    [Alias(nameof(RelayRespond))]
    Task<ChatResponse> RelayRespond(string modelName, string prompt);
}

public sealed class StreamingRelayProbe : Neuron, IStreamingRelayProbe
{
    public async Task<IReadOnlyList<ChatResponseUpdate>> CollectStreamingUpdates(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var llm = GrainFactory.GetGrain<ILlama32>(NeuronId.For<ILlama32>(Id.Owner, modelName).ToGrainId());
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in llm.RespondStreaming([new ChatMessage(ChatRole.User, prompt)]))
        {
            updates.Add(update);
        }

        return updates;
    }

    public Task<ChatResponse> RelayRespond(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var llm = GrainFactory.GetGrain<ILlama32>(NeuronId.For<ILlama32>(Id.Owner, modelName).ToGrainId());

        return llm.Respond([new ChatMessage(ChatRole.User, prompt)]);
    }
}

public sealed class LlmStreaming(ModuleFixture fixture)
{
    private const string ModelName = "streaming-model";
    private const string RelayName = "streaming-relay";
    private const string UserPrompt = "hi";
    private const string ScriptedReply = "hello world";

    [Fact(DisplayName = "ILLM.RespondStreaming yields updates whose text concatenates to the scripted reply")]
    public async Task StreamingIsPrimaryAndYieldsTheScriptedReply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedReply);

        var updates = await test.Client.Get<IStreamingRelayProbe>(RelayName)
            .CollectStreamingUpdates(ModelName, UserPrompt);

        Assert.NotEmpty(updates);
        Assert.Equal(ScriptedReply, string.Concat(updates.Select(update => update.Text)));
    }

    [Fact(DisplayName = "ILLM.Respond still returns the scripted reply after streaming inversion")]
    public async Task RespondStillReturnsScriptedReplyAfterInversion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedReply);

        var response = await test.Client.Get<ILlama32>(ModelName)
            .Respond([new ChatMessage(ChatRole.User, UserPrompt)]);

        Assert.Equal(ScriptedReply, response.Text);
    }
}
