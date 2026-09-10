namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-rejected")]
public sealed class CommandRejectedException(string message) : InvalidOperationException(message);
