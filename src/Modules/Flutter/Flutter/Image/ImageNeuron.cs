using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Image.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Image;

[GrainType(UIVocabulary.ImageType)]
internal sealed class ImageNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageState> store)
    : Neuron<ImageState>(store), IImage
{
    public Task Set(string url, string mediaType, string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(mediaType);
        ArgumentNullException.ThrowIfNull(prompt);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Url = url.Trim();
        next.MediaType = mediaType.Trim();
        next.Prompt = prompt;
        return Save(next, new ImageChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<ImageState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}