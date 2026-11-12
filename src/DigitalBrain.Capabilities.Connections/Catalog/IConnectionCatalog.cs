namespace DigitalBrain.Capabilities.Connections.Catalog;

using DigitalBrain.Brains;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;

/// <summary>Catalog neuron managing connection projections and requests for one brain.</summary>
[NeuronAlias("integrations.catalog"), DefaultNeuronType(TypeName)]
public interface IConnectionCatalog
{
    /// <summary>The neuron type name alias.</summary>
    public const string TypeName = "integrations.accounts";

    /// <summary>Builds the neuron identifier for a brain's connection catalog.</summary>
    public static NeuronId Id(string brainId) => new(new BrainId(brainId), TypeName, "main");

    /// <summary>Builds the neuron key for a brain's connection catalog.</summary>
    public static string Key(string brainId) => $"{brainId}/main";

    /// <summary>Subscribes to catalog updates.</summary>
    Task Subscribed();

    /// <summary>Reads the current connection catalog projection.</summary>
    Task<ConnectionCatalog> Read();
}
