using System.Text;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.SDK.DigitalBrain.Ai;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Voice;

public interface IVoice2Text : INeuron;

[ImplicitStreamSubscription(Voice2TextNeuronType)]
internal sealed class Voice2TextNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ISpeechToTextClient speech,
    ILogger<Voice2TextNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IVoice2Text,
      INeuronMetadata,
      IVoiceNeuron,
      IHandle<Voice2TextRequest>
{
    public const string Voice2TextNeuronType = nameof(Voice2TextNeuron);

    public static NeuronId Id => new("ai/voice/whisper");
    public static string Icon => "openai";
    public static NeuronCapability Capabilities => NeuronCapability.Voice;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        if (synapse is not Voice2TextRequest request) return;

        var options = new SpeechToTextOptions { SpeechLanguage = request.LanguageHint };

        string transcript;
        IReadOnlyList<Voice2TextSegment> segments;
        if (request.ReturnSegments)
        {
            // Microsoft.Extensions.AI 9.x exposes per-update StartTime/EndTime on
            // streaming responses only; the non-streaming SpeechToTextResponse has
            // no segment list. Drive segments off the streaming path.
            var segmentList = new List<Voice2TextSegment>();
            var transcriptBuilder = new StringBuilder();
            using var audioStream = new MemoryStream(request.Audio);
            await foreach (var update in speech.GetStreamingTextAsync(audioStream, options))
            {
                if (update.StartTime is { } start && update.EndTime is { } end)
                    segmentList.Add(new Voice2TextSegment(start, end, update.Text));
                transcriptBuilder.Append(update.Text);
            }
            transcript = transcriptBuilder.ToString();
            segments = segmentList;
        }
        else
        {
            using var audioStream = new MemoryStream(request.Audio);
            var response = await speech.GetTextAsync(audioStream, options);
            transcript = response.Text ?? string.Empty;
            segments = Array.Empty<Voice2TextSegment>();
        }

        await FireSynapseAsync(new Voice2TextResponse(Transcript: transcript,
        DetectedLanguage: request.LanguageHint ?? "auto",
        Segments: segments) { Headers = SynapseMetadata.Create(
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
}
