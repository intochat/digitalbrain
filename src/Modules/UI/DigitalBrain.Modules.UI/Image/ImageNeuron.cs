using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType(UIVocabulary.ImageType)]
internal sealed class ImageNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ImageState>> state)
    : Neuron<ImageState>(runtime, state), IImage
{
    public Task<Accepted<string>> Describe(DescribeImage command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("describe"), command, UIJson.Default.DescribeImage, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Prompt);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Model);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.MediaType);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.BlobName);
            var work = Schedule(Signal.FromJson(UIVocabulary.ImageDescribing, arguments, UIJson.Default.DescribeImage));
            return new Accepted<string>(Id.Name, work);
        });

    [ReadOnly]
    public Task<ImageState> Read() => Task.FromResult(State ?? new ImageState("", "", "", ""));

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != UIVocabulary.ImageDescribing)
        {
            return;
        }

        if (Body(delivery, UIJson.Default.DescribeImage) is not { } command)
        {
            return;
        }

        Announce(Signal.FromJson(UIVocabulary.ImageDescribed, new KitCard(Id.Name, command.Prompt), UIJson.Default.KitCard));
        await SaveAsync(new ImageState(command.Prompt, command.Model, command.MediaType, command.BlobName), cancellationToken).ConfigureAwait(true);
    }
}
