using System.Reflection;
using DigitalBrain.V2.Core.Runtime;

namespace DigitalBrain.V2.Catalog;

public sealed class CatalogNeuron : Neuron, ICatalogNeuron
{
    public Task HandleAsync(DescribeConstellation synapse, CancellationToken ct)
    {
        var assemblies = synapse.AssemblyNames
            .Distinct(StringComparer.Ordinal)
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .ToArray();

        return Emit(new ConstellationDescribed(CatalogScanner.Scan(assemblies)));
    }
}
