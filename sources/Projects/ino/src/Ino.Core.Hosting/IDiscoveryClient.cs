using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDiscoveryClient
{
    Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default);
    Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default);
    Task<IReadOnlyList<INeuronDefinition>> DumpNeuronsAsync(CancellationToken ct = default);
    void Invalidate();
}
