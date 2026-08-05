namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 honest: ModuleVersionChanged journaled on same grain; HandlerVersion stamp — not Orleans interface swap.

public sealed record AppendCrmNoteAsked(string NoteId, string ContactId, string Text) : Synapse;

public sealed record CrmNoteAck(string NoteId, string ContactId, int HandlerVersion) : Synapse;

public sealed record ModuleVersionChanged(
    string ModuleKind,
    int FromVersion,
    int ToVersion) : Synapse;

public sealed record ModuleVersionBadge(string ModuleKind, int Version) : Synapse;

public sealed class RollingCrmActivity : Neuron<RollingCrmActivityState>,
    IAnswers<AppendCrmNoteAsked, CrmNoteAck>,
    INeuron<ModuleVersionChanged>
{
    public Task<CrmNoteAck?> HandleAsync(AppendCrmNoteAsked question, CancellationToken cancellationToken)
        => Task.FromResult<CrmNoteAck?>(new CrmNoteAck(
            question.NoteId,
            question.ContactId,
            HandlerVersion: State.Version <= 0 ? 1 : State.Version));

    public Task HandleAsync(ModuleVersionChanged fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.ModuleKind, "rollingcrmactivity", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.Version = fact.ToVersion;
        Emit(new ModuleVersionBadge(fact.ModuleKind, fact.ToVersion));
        return Task.CompletedTask;
    }
}

public sealed class RollingCrmActivityState
{
    public int Version { get; set; } = 1;
}

// Caller of CRM asks (meeting notes stand-in).
public sealed class RollingMeetingNotes : Neuron, INeuron<AppendCrmNoteAsked>, INeuron<CrmNoteAck>
{
    // Session can Emit AppendCrmNoteAsked ambient — this hears and re-Asks? Better: session AskAsync.
    // Keep as catalog listener so ambient seed is legal if used; primary path is session Ask.
    public Task HandleAsync(AppendCrmNoteAsked fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CrmNoteAck fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class RollingUpgradeWatcher : Neuron, INeuron<ModuleVersionChanged>, INeuron<ModuleVersionBadge>
{
    public Task HandleAsync(ModuleVersionChanged fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ModuleVersionBadge fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
