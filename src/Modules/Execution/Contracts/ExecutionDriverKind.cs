namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.driver-kind")]
public enum ExecutionDriverKind : byte
{
    Agent = 0,
    Script = 1,
    Team = 2,
}
