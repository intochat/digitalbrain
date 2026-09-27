using System.Runtime.CompilerServices;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("ai.llm.scripted-state")]
public sealed record ScriptedLlmState
{
    [Id(0)] public IReadOnlyList<string> Replies { get; init; } = [];
    [Id(1)] public int Answered { get; init; }
    [Id(2)] public IReadOnlyList<string> Prompts { get; init; } = [];
}

[GrainType("ai.llm.scripted")]
internal sealed class ScriptedLlmNeuron(
    [PersistentState("ai.llm.scripted", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ScriptedLlmState> store)
    : Neuron, IScriptedLLM
{
    private const string ModelName = "scripted";

    public async Task Script(IReadOnlyList<string> replies)
    {
        ArgumentNullException.ThrowIfNull(replies);
        store.State = new ScriptedLlmState { Replies = replies };
        await store.WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> Prompts() => Task.FromResult(store.State.Prompts);

    public Task<ModelDescriptor> Describe(AgentModelSelection? selection = null) => Task.FromResult(new ModelDescriptor(
        ModelName, ModelName, null, "1", CapabilitySupport.Unsupported, CapabilitySupport.Unsupported, CapabilitySupport.Unsupported));

    public async Task<InferenceResult> Generate(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var script = store.State;
        if (script.Answered >= script.Replies.Count)
        { throw new InvalidOperationException($"Scripted model '{this.GetPrimaryKeyString()}' ran out of replies after {script.Answered}."); }
        var prompt = string.Join("\n", request.Messages.SelectMany(message => message.Content).OfType<AiText>().Select(text => text.Text));
        var reply = script.Replies[script.Answered];
        store.State = script with { Answered = script.Answered + 1, Prompts = [.. script.Prompts, prompt] };
        await store.WriteStateAsync(cancellationToken);
        return new InferenceResult([new AiMessage("assistant", [new AiText(reply)])], "stop", null, null, ModelName);
    }

    public async IAsyncEnumerable<InferenceUpdate> GenerateStreaming(InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await Generate(request, cancellationToken);
        yield return new InferenceUpdate("assistant", result.Messages[0].Content, result.FinishReason, null, null, Model: ModelName);
    }
}
