using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.UI;

// Same wall as IChart: Read() is the client-facing query via IEntity<TState>;
// Describe stays a same-silo grain call (kit tools drive it).
[Alias("ui.image")]
public interface IImage : IEntity<ImageState>
{
    [Alias(nameof(Describe))]
    Task Describe(ImageState state);
}
