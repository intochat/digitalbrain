using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Memory;

/// <summary>A note to forget.</summary>
[GenerateSerializer]
[Alias("memory.forget")]
public sealed record Forget(
    CommandId Id,
    [property: Id(0)] string Namespace,
    [property: Id(1)] string Key) : Command(Id);
