using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.discard-change-set")]
public sealed record DiscardChangeSet(CommandId Id) : Command(Id);
