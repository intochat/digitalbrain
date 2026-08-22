using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.UI;

[GrainType("image")]
internal sealed class ImageEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageState> state)
    : Entity<ImageState>(state), IImage
{
    public async Task Describe(ImageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await SaveAsync(state);
    }
}
