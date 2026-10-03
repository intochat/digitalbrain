using System.Runtime.CompilerServices;
using DigitalBrain;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("ai.llm.scripted-state")]
public sealed record ScriptedLlmState
{
    [Id(0)] public string[] Replies { get; init; } = [];
    [Id(1)] public int Answered { get; init; }
    [Id(2)] public string[] Prompts { get; init; } = [];
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
        store.State = new ScriptedLlmState { Replies = replies.ToArray() };
        await store.WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> Prompts() => Task.FromResult<IReadOnlyList<string>>(store.State.Prompts);

    public Task<ModelDescriptor> Describe(AgentModelSelection? selection = null) => Task.FromResult(new ModelDescriptor(
        ModelName, ModelName, null, "1", CapabilitySupport.Unsupported, CapabilitySupport.Unsupported, CapabilitySupport.Unsupported));

    public async Task<InferenceResult> Generate(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var script = store.State;
        if (script.Answered >= script.Replies.Length)
        { throw new InvalidOperationException($"Scripted model '{this.GetPrimaryKeyString()}' ran out of replies after {script.Answered}."); }
        var prompt = string.Join("\n", request.Messages.SelectMany(message => message.Content).Select(content => content switch
        {
            AiText text => text.Text,
            AiToolResult result => result.ResultJson,
            _ => null,
        }).Where(text => text is not null));
        var reply = script.Replies[script.Answered];
        store.State = script with { Answered = script.Answered + 1, Prompts = [.. script.Prompts, prompt] };
        await store.WriteStateAsync(cancellationToken);
        return new InferenceResult([new AiMessage("assistant", [ToolCall(reply) ?? (AiContent)new AiText(reply)])], "stop", null, null, ModelName);
    }

    // A scripted reply of exactly {"tool": "...", "arguments": {...}} plays a tool-calling turn, so
    // tests can drive an agent loop deterministically the way scripted text drives a completion.
    private static AiToolCall? ToolCall(string reply)
    {
        if (!reply.TrimStart().StartsWith('{')) { return null; }
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(reply);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.EnumerateObject().Count() == 2
                && document.RootElement.TryGetProperty("tool", out var tool)
                && document.RootElement.TryGetProperty("arguments", out var arguments)
                ? new AiToolCall(Guid.NewGuid().ToString("N"), tool.GetString()!, arguments.GetRawText())
                : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public async IAsyncEnumerable<InferenceUpdate> GenerateStreaming(InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await Generate(request, cancellationToken);
        yield return new InferenceUpdate("assistant", result.Messages[0].Content, result.FinishReason, null, null, Model: ModelName);
    }
}
