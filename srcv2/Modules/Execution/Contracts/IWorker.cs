using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[Alias("db.execution.worker")]
public interface IWorker : INeuron
{
    [Alias(nameof(Accept))]
    Task Accept(AttemptRequest request);

    [Alias(nameof(Continue))]
    Task Continue(AttemptCursor cursor);

    [Alias(nameof(Cancel))]
    Task Cancel(AttemptCursor cursor);
}
