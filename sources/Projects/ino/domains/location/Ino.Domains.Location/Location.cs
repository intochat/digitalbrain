using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Domains.Location;

/// <summary>
/// The Location domain. Provides per-user location memory: a journaled neuron
/// of <see cref="Contracts.LocationVisited"/> events, addressable via
/// <see cref="Contracts.ILocationNeuron"/>. Multiple plans across other
/// domains compose against this journal — taxi.ride-home reads it for home
/// + current-location anchors; later slices add behavioral-cluster mining
/// (Tokyo morning routine), co-occurrence queries (restaurant with Daria),
/// and frequency-based home inference.
///
/// No declared neurons in v0.1 — Location is substrate, not user-verb.
/// User-facing recording lands when the platform learns to passively observe
/// place visits via reactive listeners on calendar/check-in events.
/// </summary>
public sealed class Location : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Location");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities => Array.Empty<Capability>();

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons => Array.Empty<INeuronDefinition>();
}
