using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Commands;

// Replayed failures keep only error text, so the original exception type cannot be reconstructed.
[GenerateSerializer]
[Alias("db.v3.command-failed")]
public sealed class CommandFailedException(CommandId id, string message)
    : InvalidOperationException(message)
{
    [Id(0)] public CommandId Id { get; } = id;
}
