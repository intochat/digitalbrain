using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.UI;

[Alias("ui.image")]
public interface IImage : INeuron
{
    /// <summary>Describes an image and returns its instance name.</summary>
    [Alias("describe")]
    Task<Accepted<string>> Describe(DescribeImage command, CancellationToken cancellationToken = default);

    /// <summary>Reads the image description.</summary>
    [ReadOnly, Alias("read")]
    Task<ImageState> Read();
}
