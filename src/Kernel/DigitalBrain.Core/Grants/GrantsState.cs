using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("db.grants-state")]
internal sealed record GrantsState(
    [property: Id(0)] GrantRecord[] Grants)
{
    public static GrantsState Empty { get; } = new([]);
}
