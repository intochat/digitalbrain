using Ino.Core;

namespace Ino.Core.Hosting;

[GenerateSerializer]
public sealed record CanonicalTarget(
    [property: Id(0)] Type SynapseType,
    [property: Id(1)] Type GrainType,
    [property: Id(2)] DomainId Domain,
    [property: Id(3)] IReadOnlyList<Capability> RequiredCapabilities);
