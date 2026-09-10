namespace DigitalBrain.Abstractions.Commands;

// Replayed failures keep only error text, so the original exception type cannot be reconstructed.
[GenerateSerializer]
[Alias("db.v3.command-failed")]
public sealed class CommandFailedException(string message) : InvalidOperationException(message);
