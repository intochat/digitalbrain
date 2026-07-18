using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;

namespace DigitalBrain.Kernel;

public interface ITranscriptionNeuron : INeuron, IHandle<VoiceMessageRecorded> { }

// Transcription neuron: receives voice bytes (from flutter recorder via ClientTap backchannel),
// transcribes (whisper.net in prod host, mock in tests), feeds text to LLM chat + emits surface + journal.
// All via neuron/synapse. Demo mode (TestSetup) uses canned text for fast green tests without model.
[GrainType("transcription")]
public sealed class TranscriptionNeuron : Neuron, ITranscriptionNeuron
{
    private readonly Func<byte[], Task<string>> _transcribe;
    private readonly Setup _setup;

    public TranscriptionNeuron(Func<byte[], Task<string>> transcribe, Setup setup)
    {
        _transcribe = transcribe;
        _setup = setup;
    }

    public async Task HandleAsync(VoiceMessageRecorded synapse, CancellationToken cancellationToken)
    {
        var text = await _transcribe(synapse.AudioData);

        await Emit(new TranscribedText(text, "auto", null));

        // Voice becomes natural language input to the brain/LLM (same as typed chat or creator).
        var brainKey = this.GetPrimaryKeyString() ?? Brain.WellKnownKey;
        var brain = GrainFactory.GetGrain<IDigitalBrain>(brainKey);
        await brain.SendAsync(new AgentRequest(text), cancellationToken);

        // Voice surface removed (direct); rule in os/transcription.ino on: VoiceTranscribed produces "🎤 Voice" card with $text.
        await Emit(new NeuronTelemetry(Self, "VoiceTranscribed", new Dictionary<string, string>
        {
            ["length"] = text.Length.ToString(),
            ["demo"] = _setup.UseDemoMode.ToString()
        }));
    }
}