using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Diagnostics;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.SDK.DigitalBrain.Ai.Models;
using Microsoft.Extensions.AI;
using Orleans;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.NemoChat;

[GrainType("DigitalBrain.SDK.Ai.NemoChat.NemoChatNeuron")]
[ImplicitStreamSubscription(NemoChatNeuronType)]
internal sealed class NemoChatNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [Llm<NemotronMini>] IChatClient chat,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<NemoChatNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuronMetadata, IHandle<NemoChatRequest>
{
    public const string NemoChatNeuronType = nameof(NemoChatNeuron);

    public static NeuronId         Id           => new("ai/nemochat");
    public static string           Icon         => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Balanced;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        if (s is not NemoChatRequest req) return;

        var messages = new[] { new ChatMessage(ChatRole.User, req.Prompt) };
        var options = new ChatOptions { MaxOutputTokens = 500 };

        var stopwatch = Stopwatch.StartNew();

        // Trace activity
        var meta = chat.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
        using var llm = DigitalBrainTelemetry.Source.StartActivity(
            DigitalBrainTelemetry.LlmChat, ActivityKind.Client);
        
        llm?.SetTag(DigitalBrainTelemetry.GenAiOperationName, "chat");
        llm?.SetTag(DigitalBrainTelemetry.GenAiSystem, meta?.ProviderName ?? "ollama");
        llm?.SetTag(DigitalBrainTelemetry.GenAiRequestModel, meta?.DefaultModelId ?? "nemotron-mini");
        llm?.SetTag(DigitalBrainTelemetry.TagNeuronType, NemoChatNeuronType);
        llm?.SetTag(DigitalBrainTelemetry.TagCorrelation, req.CorrelationId);

        var response = await chat.GetResponseAsync(messages, options);
        stopwatch.Stop();

        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        var inputTokens = response.Usage?.InputTokenCount ?? 12; // fallback for mock
        var outputTokens = response.Usage?.OutputTokenCount ?? (response.Text?.Length / 4 ?? 25);
        var tps = outputTokens / (durationMs / 1000.0);

        llm?.SetTag(DigitalBrainTelemetry.GenAiInputTokens, inputTokens);
        llm?.SetTag(DigitalBrainTelemetry.GenAiOutputTokens, outputTokens);

        // Build the telemetry payload
        var telemetry = new
        {
            provider = meta?.ProviderName ?? "ollama",
            model = meta?.DefaultModelId ?? "nemotron-mini",
            inputTokens = inputTokens,
            outputTokens = outputTokens,
            duration = $"{durationMs:F1} ms",
            speed = $"{tps:F1} t/s"
        };

        var cardJson = JsonSerializer.Serialize(new
        {
            prompt = req.Prompt,
            response = response.Text ?? "No response from local Nemo LLM.",
            telemetry = telemetry
        });

        // Fire the RfwCard Synapse
        await FireSynapseAsync(new RfwCard(
            LibraryName: "digitalbrain",
            RootWidget: "NemoChatCard",
            DataJson: cardJson
        ) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: NemoChatNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: time.GetUtcNow()
        ) });
    }
}
