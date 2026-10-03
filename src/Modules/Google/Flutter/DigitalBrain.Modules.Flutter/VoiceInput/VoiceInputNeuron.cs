using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.VoiceInput.Signals;
using DigitalBrain.Kernel;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.VoiceInput;

[GrainType(UIVocabulary.VoiceInputType)]
internal sealed class VoiceInputNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<VoiceInputState> store)
    : Neuron<VoiceInputState>(store), IVoiceInput
{
    public Task Configure(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var next = Next();
        next.Label = label.Trim();
        return Save(next, new VoiceInputChanged(next.Name, next.Revision));
    }

    public async Task Capture(byte[] audio, string mimeType)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (audio.Length is 0 or > IVoiceInput.MaxAudioBytes) { throw new ArgumentException("Audio must contain between 1 byte and 8 MiB.", nameof(audio)); }
        if (!mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) { throw new ArgumentException("Only audio can be captured.", nameof(mimeType)); }
        var next = Next();
        next.Captures++;
        var captured = new VoiceCaptured(next.Name, audio, mimeType.Trim().ToLowerInvariant());
        await Save(next, new VoiceInputChanged(next.Name, next.Revision), captured);
        await GrainFactory.GetGrain<IUiBinding>(next.Name).Dispatch(captured);
    }

    [ReadOnly] public Task<VoiceInputState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }

    private VoiceInputState Next()
    {
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Revision++;
        return next;
    }
}
