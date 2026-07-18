using Ino.Core;

namespace Ino.Core.Hosting;

[GenerateSerializer]
public sealed record ReactiveRegistration(
    [property: Id(0)] Type SynapseType,
    [property: Id(1)] Type GrainType,
    [property: Id(2)] DomainId Domain);
