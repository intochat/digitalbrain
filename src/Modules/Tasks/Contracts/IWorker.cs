using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[Alias("DigitalBrain.Tasks.IWorker")]
public interface IWorker : INeuron
{
    [Alias(nameof(Accept))]
    Task Accept(AttemptRequest request);

    [Alias(nameof(Continue))]
    Task Continue(AttemptCursor cursor);

    [Alias(nameof(Cancel))]
    Task Cancel(AttemptCursor cursor);
}
