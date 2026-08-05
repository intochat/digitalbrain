using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record TaskCreated(
    string TaskId,
    string Title,
    string SourceMessageId,
    string Tag) : Synapse;

public sealed record BehaviorNudge(
    string BehaviorId,
    string MessageId,
    string ChipLabel) : Synapse;

// Catalog listeners for ambient script emissions (Emit requires declared hearers).
public sealed class TaskStore : Neuron, INeuron<TaskCreated>
{
    public Task HandleAsync(TaskCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class UiProjector : Neuron, INeuron<BehaviorNudge>
{
    public Task HandleAsync(BehaviorNudge fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Stand-in for an installed behavior script: INeuron<EmailReceived> that materializes
// finance task + UI chip when the subject contains "Invoice". Body is data only — never eval'd.
public sealed class InvoiceCatcher : Neuron, INeuron<EmailReceived>
{
    public const string BehaviorId = "invoice-catcher";

    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        if (!fact.Subject.Contains("Invoice", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        Emit(new TaskCreated(
            TaskId: $"task-{fact.MessageId}",
            Title: fact.Subject,
            SourceMessageId: fact.MessageId,
            Tag: "finance"));
        Emit(new BehaviorNudge(
            BehaviorId,
            fact.MessageId,
            ChipLabel: "Invoice"));
        return Task.CompletedTask;
    }
}
