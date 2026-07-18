using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;

namespace DigitalBrain.V2.Catalog;

public interface ICatalogNeuron : INeuron, IHandle<DescribeConstellation>, IEmit<ConstellationDescribed>;

[GenerateSerializer]
public sealed record DescribeConstellation([property: Id(0)] string[] AssemblyNames) : Synapse;

[GenerateSerializer]
public sealed record ConstellationDescribed([property: Id(0)] CatalogDocument Catalog) : Synapse;
