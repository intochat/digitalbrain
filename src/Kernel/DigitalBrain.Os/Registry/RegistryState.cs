using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.Os;

[GenerateSerializer]
[Alias("db.registry-state")]
internal sealed record RegistryState(
    [property: Id(0)] RegisteredInstance[] Instances)
{
    public static RegistryState Empty { get; } = new([]);
}
