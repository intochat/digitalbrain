namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.state")]
public enum TaskState
{
    Pending,
    Running,
    Waiting,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
}
