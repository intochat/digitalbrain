using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-rejected")]
public sealed class CommandRejectedException(CommandId id, string reason, string message)
    : InvalidOperationException(message)
{
    [Id(0)] public CommandId Id { get; } = id;
    [Id(1)] public string Reason { get; } = reason;
}
