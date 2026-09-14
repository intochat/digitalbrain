using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.promote-slot")]
public sealed record PromoteSlot(CommandId Id) : Command(Id);
