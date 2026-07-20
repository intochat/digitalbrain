using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[Alias("tasks.task")]
public interface ITask : INeuron
{
    [Alias("Start")]
    Task<TaskSnapshot> StartAsync(StartTask command);

    [Alias("Cancel")]
    Task<TaskSnapshot> CancelAsync(CancelTask command);

    [Alias("Read")]
    Task<TaskSnapshot> ReadAsync();
}
