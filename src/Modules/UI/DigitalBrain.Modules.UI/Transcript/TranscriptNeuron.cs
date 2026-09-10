using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.TranscriptType)]
internal sealed class TranscriptNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TranscriptState>> state)
    : Neuron<TranscriptState>(runtime, state), ITranscript
{
    public Task<Accepted<string>> Append(AppendTranscript command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("append"), command, UIJson.Default.AppendTranscript, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentNullException.ThrowIfNull(arguments.Entry);
            var work = Schedule(UIBodies.Signal(UIVocabulary.TranscriptAppending, arguments, UIJson.Default.AppendTranscript));
            return new Accepted<string>(Id.Name, work);
        });

    [ReadOnly]
    public Task<TranscriptState> Read() => Task.FromResult(State ?? new TranscriptState([]));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != UIVocabulary.TranscriptAppending)
        {
            return;
        }

        var command = UIBodies.Read(delivery, UIJson.Default.AppendTranscript);
        await SaveAsync(new TranscriptState(BoundedList.Append(State?.Entries ?? [], command.Entry, 500)),
            cancellationToken).ConfigureAwait(true);
    }
}
