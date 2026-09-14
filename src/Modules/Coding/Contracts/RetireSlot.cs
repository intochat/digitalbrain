using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.retire-slot")]
public sealed record RetireSlot(CommandId Id) : Command(Id);
