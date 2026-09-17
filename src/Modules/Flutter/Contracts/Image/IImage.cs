using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter;

[Alias("ui.image")]
public interface IImage : INeuron
{
    /// <summary>Describes an image and returns its instance name.</summary>
    [Alias("describe")]
    [NeuronTool]
    Task<Accepted<string>> Describe(DescribeImage command, CancellationToken cancellationToken = default);

    /// <summary>Reads the image description.</summary>
    [ReadOnly, Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<ImageState> Read();
}
