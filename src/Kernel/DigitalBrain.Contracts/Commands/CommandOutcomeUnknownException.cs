using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-outcome-unknown")]
public sealed class CommandOutcomeUnknownException(CommandId id, string message)
    : InvalidOperationException(message)
{
    [Id(0)] public CommandId Id { get; } = id;
}
