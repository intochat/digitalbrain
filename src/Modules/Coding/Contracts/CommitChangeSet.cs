using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.commit-change-set")]
public sealed record CommitChangeSet(CommandId Id, [property: Id(0)] string Message) : Command(Id);
