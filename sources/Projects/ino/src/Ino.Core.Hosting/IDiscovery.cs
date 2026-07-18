using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDiscovery : IGrainWithIntegerKey
{
    Task RegisterAsync(SiloRegistration registration, CancellationToken ct = default);
    Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default);
    Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default);
    Task<DiscoveryDump> DumpAsync(CancellationToken ct = default);
    Task<IReadOnlyList<INeuronDefinition>> DumpNeuronsAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers a dynamic neuron created at runtime by the L1 loop's
    /// <c>CreatorNeuron</c> (<c>Ino.Domains.Genesis</c>). Idempotent per
    /// <see cref="INeuronDefinition.Id"/>; subsequent calls with the same id
    /// replace the prior entry. Discovery merges these into the per-silo
    /// static set on every <see cref="DumpNeuronsAsync"/>, so Cortex
    /// sees them on the next routing pass without a silo restart.
    /// </summary>
    Task RegisterDynamicNeuronAsync(INeuronDefinition neuron, CancellationToken ct = default);
}
