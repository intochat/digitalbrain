using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.check-change-set")]
public sealed record CheckChangeSet(CommandId Id) : Command(Id);
