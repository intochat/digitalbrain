using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[Alias("tasks.worker")]
public interface IWorker : INeuron
{
    [Alias("Accept")]
    Task AcceptAsync(AttemptRequest request);

    [Alias("Continue")]
    Task ContinueAsync(AttemptCursor cursor);

    [Alias("Cancel")]
    Task CancelAsync(AttemptCursor cursor);
}
