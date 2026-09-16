namespace DigitalBrain.Abstractions.Commands;

[GenerateSerializer]
[Alias("db.v3.command-phase")]
public enum CommandPhase
{
    Rejected,
    Attempted,
    Completed,
    Failed,
    Unknown,
}
