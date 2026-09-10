namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-outcome-unknown")]
public sealed class CommandOutcomeUnknownException(string message) : InvalidOperationException(message);
