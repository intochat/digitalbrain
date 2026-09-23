using DigitalBrain.Contracts;

namespace DigitalBrain.Automations;

// The automation validates its action against this catalog before activation. The Apps module
// owns the real lookup; the parallel P2.6 work supplies it and this seam keeps the dependency one-way.
public interface IAutomationActionCatalog
{
    Task<AutomationOperation?> FindAsync(string appId, string operation, CancellationToken cancellationToken = default);
}

// A paid or side-effect step is never retried automatically: the invoker is called at most once
// per idempotency key (automation id, scheduled time).
public interface IAutomationActionInvoker
{
    Task<AutomationActionResult> InvokeAsync(AutomationDefinition definition, AutomationAction action, string intentId, CancellationToken cancellationToken = default);
}

// Run history goes to durable receipts; the result or failure reaches the Inbox. The Inbox and
// Receipts implementations are built in parallel, so this journal is the single seam they plug into.
public interface IAutomationJournal
{
    Task RecordRunAsync(AutomationDefinition definition, JobRun run, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("automations.changed")]
public sealed record AutomationChanged(string AutomationId, bool Active) : Signal;

[GenerateSerializer, Alias("automations.run-completed")]
public sealed record AutomationRunCompleted(string AutomationId, JobRun Run) : Signal;

// Keyed by automation id. The same key names the ReminderNeuron that drives the schedule, so a
// durable reminder wakes this neuron even after a silo restart.
[Alias("automation")]
[Orleans.Metadata.DefaultGrainType("automation")]
public interface IAutomation : INeuron
{
    Task<AutomationValidationResult> Save(AutomationDefinition definition);

    Task<AutomationValidationResult> Activate();

    Task Deactivate();

    Task<AutomationSnapshot?> Read();

    Task Delete();

    Task<JobRun> RunNow(DateTimeOffset scheduledFor);

    Task<IReadOnlyList<JobRun>> ReadRuns();
}
