using System.Collections.Immutable;
using Ino.Core;

namespace Ino.Core.Hosting;

public interface IDomain
{
    DomainId Id { get; }
    string Version { get; }
    IReadOnlyList<Capability> DeclaredCapabilities { get; }

    IReadOnlyList<INeuronDefinition> DeclaredNeurons => Array.Empty<INeuronDefinition>();

    // Optional — domains without per-grain detail return an empty dictionary.
    // Phase 2 enforcement is domain-level; Phase 3 source gen may populate this automatically.
    IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities
        => ImmutableDictionary<Type, IReadOnlyList<Capability>>.Empty;
}
