using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Twitter;

[Alias("twitter")]
public interface ITwitter : INeuron
{
    /// <summary>Accepts an authenticated provider receipt for the account named by this neuron.</summary>
    [Alias("accept")]
    Task<Accepted<Posted>> Accept(Post command);

    [ReadOnly]
    [Alias("read")]
    Task<TwitterSnapshot> Read();
}
