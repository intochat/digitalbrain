using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain;

/// <summary>
/// The current state record of a state alias, read at that alias's state neuron (<c>{brain}/{alias}/default</c>): what a debugger's
/// inspector shows for a unit. Routed like <see cref="IViewReader"/>.
/// </summary>
[NeuronAlias("synapse.state")]
public interface IStateReader
{
    /// <summary>
    /// The state record of <paramref name="alias"/> as JSON (the runtime wire shape of the record), or JSON <c>null</c> when the neuron
    /// is not that alias's. A state never saved reads as the record's default.
    /// </summary>
    /// <param name="alias">The state alias.</param>
    [ReadOnlyCall]
    Task<JsonElement> Read(string alias);
}
