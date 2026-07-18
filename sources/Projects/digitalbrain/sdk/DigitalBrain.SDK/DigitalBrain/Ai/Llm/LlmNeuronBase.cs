using System.Diagnostics;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Llm;

internal abstract class LlmNeuronBase(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IChatClient chat,
    ILogger logger)
    : global::DigitalBrain.Runtime.Neurons.Neuron(incoming, outgoing, grains, logger),
      IHandle<LlmRequest>
{
    protected IChatClient Chat { get; } = chat;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is not LlmRequest request) return;

        var messages = new List<ChatMessage>(request.Messages.Count + 1);
        if (!string.IsNullOrEmpty(request.System))
            messages.Add(new ChatMessage(ChatRole.System, request.System));
        foreach (var m in request.Messages)
            messages.Add(new ChatMessage(RoleFor(m.Role), m.Content));

        var options = new ChatOptions
        {
            Temperature = request.Temperature,
            MaxOutputTokens = request.MaxOutputTokens,
        };

        // One GenAI-semconv span around the IChatClient call — the HttpClient
        // span the provider emits nests under it, and it nests under the
        // caller's neuron.handle span, so an LLM hop is one link in the trace.
        var meta = Chat.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        using var llm = DigitalBrainTelemetry.Source.StartActivity(
            DigitalBrainTelemetry.LlmChat, ActivityKind.Client);
        llm?.SetTag(DigitalBrainTelemetry.GenAiOperationName, "chat");
        llm?.SetTag(DigitalBrainTelemetry.GenAiSystem, meta?.ProviderName ?? "unknown");
        llm?.SetTag(DigitalBrainTelemetry.GenAiRequestModel, meta?.DefaultModelId ?? NeuronType);
        llm?.SetTag(DigitalBrainTelemetry.TagNeuronType, NeuronType);
        llm?.SetTag(DigitalBrainTelemetry.TagCorrelation, request.CorrelationId);

        var response = await Chat.GetResponseAsync(messages, options);

        llm?.SetTag(DigitalBrainTelemetry.GenAiInputTokens, response.Usage?.InputTokenCount);
        llm?.SetTag(DigitalBrainTelemetry.GenAiOutputTokens, response.Usage?.OutputTokenCount);

        await FireSynapseAsync(new LlmResponse(Text: response.Text ?? string.Empty,
        FinishReason: response.FinishReason?.ToString(),
        InputTokens: response.Usage?.InputTokenCount,
        OutputTokens: response.Usage?.OutputTokenCount) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }

    static ChatRole RoleFor(string role) => role.ToLowerInvariant() switch
    {
        "system" => ChatRole.System,
        "assistant" => ChatRole.Assistant,
        "tool" => ChatRole.Tool,
        _ => ChatRole.User,
    };
}
