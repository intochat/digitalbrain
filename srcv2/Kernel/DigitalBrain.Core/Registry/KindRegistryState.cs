using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.kind-registry-state")]
internal sealed record KindRegistryState(
    [property: Id(0)] KindRecord[] Installed)
{
    public static KindRegistryState Empty { get; } = new([]);
}
