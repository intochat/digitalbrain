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
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageState> state)
    : Neuron<ImageState>(runtime, state), IImage
{
    public Task<Accepted<string>> Describe(DescribeImage command, CancellationToken cancellationToken = default)
        => ExecuteCommandAsync(Descriptor("describe"), command, UIJson.Default.DescribeImage, UIJson.Default.AcceptedString, arguments =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Prompt);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.Model);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.MediaType);
            ArgumentException.ThrowIfNullOrWhiteSpace(arguments.BlobName);
            var work = Schedule(UIBodies.Signal(UIVocabulary.ImageDescribing, arguments, UIJson.Default.DescribeImage));
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

        var command = UIBodies.Read(delivery, UIJson.Default.DescribeImage);
        await SaveAsync(new ImageState(command.Prompt, command.Model, command.MediaType, command.BlobName), cancellationToken).ConfigureAwait(true);
        await FireAsync(UIBodies.Card(UIVocabulary.ImageDescribed, Id.Name, command.Prompt),
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
