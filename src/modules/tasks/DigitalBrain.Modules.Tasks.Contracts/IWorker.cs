using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[Description("Task worker attempt execution neuron")]
public partial interface IWorker : INeuron
{
    [Alias(nameof(Accept))]
    Task Accept(AttemptRequest request);

    [Alias(nameof(Continue))]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Continue is the ratified domain verb for advancing an accepted task attempt.")]
    Task Continue(AttemptCursor cursor);

    [Alias(nameof(Cancel))]
    Task Cancel(AttemptCursor cursor);
}
