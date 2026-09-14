using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

// Generation is the git generation the slot is built from (D3); ChangedFiles are the files the landing
// wrote, and they decide whether a rollback to the previous slot stays allowed.
[GenerateSerializer]
[Alias("coding.build-slot")]
public sealed record BuildSlot(
    CommandId Id,
    [property: Id(0)] string? Generation = null,
    [property: Id(1)] IReadOnlyList<string>? ChangedFiles = null) : Command(Id);
