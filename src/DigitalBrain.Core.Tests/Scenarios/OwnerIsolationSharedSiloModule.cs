using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 multi-owner: isolation is context Name on NeuronId, not an OwnerId field.

public sealed record OwnerMailLogged(string Owner, string MessageId, string Subject) : Synapse;

public sealed record OwnerJournalRangeAsked(string Owner, string About) : Synapse;

public sealed record OwnerJournalSlice(
    string Owner,
    IReadOnlyList<string> MessageIds,
    IReadOnlyList<string> Subjects) : Synapse;

public sealed record CrossOwnerAttemptObserved(
    string OffenderOwner,
    string TargetOwner,
    string Reason) : Synapse;

// Per-context mail sink: hears EmailReceived only in its own context Name.
public sealed class OwnerInbox : Neuron<OwnerInboxState>, INeuron<EmailReceived>
{
    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        State.MessageIds.Add(fact.MessageId);
        State.Subjects.Add(fact.Subject);
        Emit(new OwnerMailLogged(Id.Name, fact.MessageId, fact.Subject));
        return Task.CompletedTask;
    }
}

public sealed class OwnerInboxState
{
#pragma warning disable CA1002, CA2227
    public List<string> MessageIds { get; set; } = [];
    public List<string> Subjects { get; set; } = [];
#pragma warning restore CA1002, CA2227
}

// Per-context journal query: answers only from own durable state (never foreign context).
public sealed class OwnerJournalQuery : Neuron<OwnerInboxState>,
    INeuron<OwnerMailLogged>,
    IAnswers<OwnerJournalRangeAsked, OwnerJournalSlice>
{
    public Task HandleAsync(OwnerMailLogged fact, CancellationToken cancellationToken)
    {
        // Mirror mailbox log into query state when ambient mail log lands in this context.
        if (!State.MessageIds.Contains(fact.MessageId, StringComparer.Ordinal))
        {
            State.MessageIds.Add(fact.MessageId);
            State.Subjects.Add(fact.Subject);
        }

        return Task.CompletedTask;
    }

    public Task<OwnerJournalSlice?> HandleAsync(
        OwnerJournalRangeAsked question, CancellationToken cancellationToken)
    {
        // Stage-1 fence: answer only when the asker's context Name matches this grain Name.
        // Foreign directed asks still hit a different Name activation with empty state.
        return Task.FromResult<OwnerJournalSlice?>(
            new OwnerJournalSlice(Id.Name, [.. State.MessageIds], [.. State.Subjects]));
    }
}

public sealed class OwnerMailLedger : Neuron, INeuron<OwnerMailLogged>, INeuron<CrossOwnerAttemptObserved>
{
    public Task HandleAsync(OwnerMailLogged fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CrossOwnerAttemptObserved fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Catalog sink so session can emit CrossOwnerAttemptObserved in isolation proofs.
public sealed class OwnerSecurityAudit : Neuron, INeuron<CrossOwnerAttemptObserved>
{
    public Task HandleAsync(CrossOwnerAttemptObserved fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
