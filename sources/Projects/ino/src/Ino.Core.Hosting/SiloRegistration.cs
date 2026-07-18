using Ino.Core;

namespace Ino.Core.Hosting;

[GenerateSerializer]
public sealed record SiloRegistration(
    [property: Id(0)] DomainId Silo,
    [property: Id(1)] IReadOnlyList<CanonicalRegistration> Canonical,
    [property: Id(2)] IReadOnlyList<ReactiveRegistration> Reactive,
    [property: Id(3)] IReadOnlyList<INeuronDefinition> Neurons);
