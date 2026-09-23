using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Time.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.Automations;

// Keyed by automation id. The durable reminder on the same key drives the schedule; the run
// history keyed by (automation id, scheduled time) makes every run idempotent across restarts.
[GrainType("automation")]
internal sealed class AutomationNeuron : Neuron<AutomationState>, IAutomation, IReminderJob, INeuronObserver
{
    private readonly IPersistentState<AutomationState> state;
    private INeuron? eventSource;

    public AutomationNeuron([PersistentState("state", "Default")] IPersistentState<AutomationState> state) : base(state)
        => this.state = state;

    private IAutomationActionCatalog Catalog => ServiceProvider.GetRequiredService<IAutomationActionCatalog>();
    private IAutomationActionInvoker Invoker => ServiceProvider.GetRequiredService<IAutomationActionInvoker>();
    private IAutomationJournal Journal => ServiceProvider.GetRequiredService<IAutomationJournal>();

    public async Task<AutomationValidationResult> Save(AutomationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!string.Equals(definition.Id, this.GetPrimaryKeyString(), StringComparison.Ordinal))
        { return AutomationValidationResult.Rejected("Definition id must match the automation id."); }
        if (string.IsNullOrWhiteSpace(definition.Name)) { return AutomationValidationResult.Rejected("Name is required."); }
        if (definition.ComputeBudget < 0) { return AutomationValidationResult.Rejected("Compute budget cannot be negative."); }
        if (definition.Trigger.Kind == AutomationTriggerKind.Schedule && definition.Trigger.IntervalSeconds < 1)
        { return AutomationValidationResult.Rejected("A schedule needs an interval of at least one second."); }
        if (definition.Trigger.Kind == AutomationTriggerKind.Event
            && (string.IsNullOrWhiteSpace(definition.Trigger.EventSourceId) || string.IsNullOrWhiteSpace(definition.Trigger.EventSignalType)))
        { return AutomationValidationResult.Rejected("An event trigger needs a source neuron and a signal type."); }

        await Save(Copy(Snapshot, definition: definition), new AutomationChanged(definition.Id, Snapshot.Active));
        if (Snapshot.Active) { await DisarmAsync(); await ArmAsync(definition); }
        return AutomationValidationResult.Ok;
    }

    public async Task<AutomationValidationResult> Activate()
    {
        var definition = Snapshot.Definition;
        if (definition is null) { return AutomationValidationResult.Rejected("Save a definition before activating."); }
        var operation = await Catalog.FindAsync(definition.Action.AppId, definition.Action.Operation).ConfigureAwait(true);
        if (operation is null)
        { return AutomationValidationResult.Rejected($"Unknown app operation {definition.Action.AppId}.{definition.Action.Operation}."); }

        await DisarmAsync();
        await ArmAsync(definition);
        await Save(Copy(Snapshot, active: true), new AutomationChanged(definition.Id, true));
        return AutomationValidationResult.Ok;
    }

    public async Task Deactivate()
    {
        await DisarmAsync();
        await Save(Copy(Snapshot, active: false), new AutomationChanged(this.GetPrimaryKeyString(), false));
    }

    public Task<AutomationSnapshot?> Read()
        => Task.FromResult(Snapshot.Definition is { } definition
            ? new AutomationSnapshot
            {
                Definition = definition,
                Active = Snapshot.Active,
                SpentCompute = Snapshot.SpentCompute,
                Runs = [.. Snapshot.Runs.Values.OrderBy(run => run.ScheduledFor)],
            }
            : null);

    public async Task Delete()
    {
        await DisarmAsync();
        await state.ClearStateAsync();
        await PublishAsync(new AutomationChanged(this.GetPrimaryKeyString(), false));
    }

    public Task<IReadOnlyList<JobRun>> ReadRuns()
        => Task.FromResult<IReadOnlyList<JobRun>>([.. Snapshot.Runs.Values.OrderBy(run => run.ScheduledFor)]);

    public Task<JobRun> RunNow(DateTimeOffset scheduledFor) => ExecuteSlotAsync(scheduledFor);

    public Task RunJob(DateTimeOffset scheduledFor)
        => Snapshot is { Active: true, Definition: not null } ? ExecuteSlotAsync(scheduledFor) : Task.CompletedTask;

    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        var definition = Snapshot.Definition;
        if (definition is null || !Snapshot.Active || definition.Trigger.Kind != AutomationTriggerKind.Event)
        { return Task.CompletedTask; }
        if (definition.Trigger.EventSignalType is { } expected
            && !string.Equals(signal.GetType().Name, expected, StringComparison.Ordinal))
        { return Task.CompletedTask; }
        return ExecuteSlotAsync(DateTimeOffset.UtcNow);
    }

    private async Task<JobRun> ExecuteSlotAsync(DateTimeOffset scheduledFor)
    {
        var definition = Snapshot.Definition
            ?? throw new InvalidOperationException("The automation has no definition.");
        var slot = ScheduledSlot(scheduledFor, definition);
        if (Snapshot.Runs.TryGetValue(slot.UtcTicks, out var recorded)) { return recorded; }

        var intentId = $"automation:{definition.Id}:{slot.UtcTicks}";
        if (Snapshot.SpentCompute + definition.Action.EstimatedCompute > definition.ComputeBudget)
        {
            return await CommitAsync(definition, Run(definition, slot, intentId, JobOutcome.SkippedBudget, "Compute budget exceeded.")).ConfigureAwait(true);
        }

        var result = await Invoker.InvokeAsync(definition, definition.Action, intentId).ConfigureAwait(true);
        var outcome = result.Succeeded ? JobOutcome.Succeeded : JobOutcome.Failed;
        var run = Run(definition, slot, intentId, outcome, result.Succeeded ? null : result.Error ?? "The action failed.");
        return await CommitAsync(definition, run, result.Succeeded ? result.ActualCompute : 0m).ConfigureAwait(true);
    }

    private async Task<JobRun> CommitAsync(AutomationDefinition definition, JobRun run, decimal compute = 0m)
    {
        var next = Copy(Snapshot);
        next.SpentCompute += compute;
        next.Runs[run.ScheduledFor.UtcTicks] = run;
        await Save(next, new AutomationChanged(definition.Id, next.Active), new AutomationRunCompleted(definition.Id, run));
        await Journal.RecordRunAsync(definition, run).ConfigureAwait(true);
        return run;
    }

    private async Task ArmAsync(AutomationDefinition definition)
    {
        if (definition.Trigger.Kind == AutomationTriggerKind.Schedule)
        {
            var reminder = GrainFactory.GetGrain<IReminder>(this.GetPrimaryKeyString());
            await reminder.StartJob(TimeSpan.Zero, TimeSpan.FromSeconds(Math.Max(1, definition.Trigger.IntervalSeconds)));
        }
        else if (definition.Trigger.Kind == AutomationTriggerKind.Event && definition.Trigger.EventSourceId is { } sourceId)
        {
            eventSource = GrainFactory.GetGrain<INeuron>(sourceId);
            await eventSource.Watch(this.AsReference<INeuronObserver>());
        }
    }

    private async Task DisarmAsync()
    {
        await GrainFactory.GetGrain<IReminder>(this.GetPrimaryKeyString()).StopJob();
        if (eventSource is not null)
        {
            await eventSource.Unwatch(this.AsReference<INeuronObserver>());
            eventSource = null;
        }
    }

    private static DateTimeOffset ScheduledSlot(DateTimeOffset observedAt, AutomationDefinition definition)
    {
        var seconds = definition.Trigger.Kind == AutomationTriggerKind.Schedule
            ? Math.Max(1, definition.Trigger.IntervalSeconds)
            : 1;
        var width = TimeSpan.FromSeconds(seconds).Ticks;
        return new DateTimeOffset(observedAt.UtcTicks / width * width, TimeSpan.Zero);
    }

    private static JobRun Run(AutomationDefinition definition, DateTimeOffset scheduledFor, string intentId, JobOutcome outcome, string? firstError)
        => new()
        {
            AutomationId = definition.Id,
            ScheduledFor = scheduledFor,
            WorkspaceId = definition.WorkspaceId,
            IntentId = intentId,
            Outcome = outcome,
            FirstError = firstError,
        };

    private static AutomationState Copy(AutomationState source, AutomationDefinition? definition = null, bool? active = null)
        => new()
        {
            Definition = definition ?? source.Definition,
            Active = active ?? source.Active,
            SpentCompute = source.SpentCompute,
            Runs = new Dictionary<long, JobRun>(source.Runs),
        };
}
