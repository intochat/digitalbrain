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

    [Alias(nameof(DrainStreamingOneUpdatePerBatch))]
    Task<int> DrainStreamingOneUpdatePerBatch(string modelName, string prompt);

    [Alias(nameof(AbandonStreamingAfterFirstUpdate))]
    Task AbandonStreamingAfterFirstUpdate(string modelName, string prompt);

    [Alias(nameof(StreamFromTargetWithoutTheContract))]
    Task StreamFromTargetWithoutTheContract(string relayName);

    [Alias(nameof(CountPendingStreamedRequests))]
    Task<int> CountPendingStreamedRequests();

    [Alias(nameof(RelayRespond))]
    Task<ChatResponse> RelayRespond(string modelName, string prompt);
}

public sealed class StreamingRelayProbe : Neuron, IStreamingRelayProbe
{
    public async Task<IReadOnlyList<ChatResponseUpdate>> CollectStreamingUpdates(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in Model(modelName).RespondStreaming([new ChatMessage(ChatRole.User, prompt)]))
        {
            updates.Add(update);
        }

        return updates;
    }

    public async Task<int> DrainStreamingOneUpdatePerBatch(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var drained = 0;

        await foreach (var _ in Model(modelName)
            .RespondStreaming([new ChatMessage(ChatRole.User, prompt)])
            .WithBatchSize(1))
        {
            drained++;
        }

        return drained;
    }

    public async Task AbandonStreamingAfterFirstUpdate(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        await foreach (var _ in Model(modelName)
            .RespondStreaming([new ChatMessage(ChatRole.User, prompt)])
            .WithBatchSize(1))
        {
            break;
        }
    }

    public async Task StreamFromTargetWithoutTheContract(string relayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relayName);

        var mismatched = GrainFactory.GetGrain<ILLM>(
            NeuronId.For<IStreamingRelayProbe>(Id.Owner, relayName).ToGrainId());

        await foreach (var _ in mismatched.RespondStreaming([new ChatMessage(ChatRole.User, "hi")]))
        {
        }
    }

    public Task<int> CountPendingStreamedRequests() => Task.FromResult(PendingStreamedCapabilityRequests);

    public Task<ChatResponse> RelayRespond(string modelName, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return Model(modelName).Respond([new ChatMessage(ChatRole.User, prompt)]);
    }

    private ILlama32 Model(string modelName)
        => GrainFactory.GetGrain<ILlama32>(NeuronId.For<ILlama32>(Id.Owner, modelName).ToGrainId());
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

        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in test.Client.GetGrainProxy<ILlama32>(ModelName)
            .RespondStreaming([new ChatMessage(ChatRole.User, UserPrompt)], cancellationToken))
        {
            updates.Add(update);
        }

        Assert.NotEmpty(updates);
        Assert.Equal(ScriptedReply, string.Concat(updates.Select(update => update.Text)));
    }

    [Fact(DisplayName = "a neuron relaying ILLM.RespondStreaming yields the same scripted reply")]
    public async Task RelayedStreamingYieldsTheScriptedReply()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedReply);

        var updates = await test.Client.GetGrainProxy<IStreamingRelayProbe>(RelayName)
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

        var response = await test.Client.GetGrainProxy<ILlama32>(ModelName)
            .Respond([new ChatMessage(ChatRole.User, UserPrompt)]);

        Assert.Equal(ScriptedReply, response.Text);
    }
}
