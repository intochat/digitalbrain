namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 hot-reload vocabulary: Connect rewires live routing; no ALC unload.

public sealed record InflightObserveEmail(
    string MessageId,
    string Domain,
    string Subject) : Synapse;

public sealed record InflightEmailReceived(
    string MessageId,
    string Domain,
    string Subject) : Synapse;

public sealed record AccountLookupAsked(string MessageId, string Domain) : Synapse;

public sealed record AccountLookupAnswered(string MessageId, string AccountName) : Synapse;

public sealed record InflightTaskCreated(string TaskId, string MessageId, string Rev) : Synapse;

public sealed record BehaviorSuperseded(string OldRev, string NewRev, string Policy) : Synapse;

public sealed record BehaviorGenerationDrained(string Rev, string MessageId) : Synapse;

public sealed record InflightCrmUnblock(string MessageId) : Synapse;

// Ingress that Connect-routes InflightEmailReceived to the active behavior generation name.
public sealed class InflightMailHub : Neuron, INeuron<InflightObserveEmail>
{
    public Task HandleAsync(InflightObserveEmail fact, CancellationToken cancellationToken)
    {
        Emit(new InflightEmailReceived(fact.MessageId, fact.Domain, fact.Subject));
        return Task.CompletedTask;
    }
}

// Behavior generation grain: Name is the rev (rev1/rev2). Open ask completes on this activation.
public sealed class InflightBehavior : Neuron, INeuron<InflightEmailReceived>, INeuron<AccountLookupAnswered>
{
    public Task HandleAsync(InflightEmailReceived fact, CancellationToken cancellationToken)
    {
        Ask<AccountLookupAnswered>(new AccountLookupAsked(fact.MessageId, fact.Domain));
        return Task.CompletedTask;
    }

    public Task HandleAsync(AccountLookupAnswered fact, CancellationToken cancellationToken)
    {
        var rev = Id.Name;
        Emit(new InflightTaskCreated($"task-{fact.MessageId}", fact.MessageId, rev));
        Emit(new BehaviorGenerationDrained(rev, fact.MessageId));
        return Task.CompletedTask;
    }
}

// Deferred CRM: null reply holds open ask until InflightCrmUnblock Emit of the answer.
public sealed class InflightSlowCrm : Neuron<InflightSlowCrmState>,
    IAnswers<AccountLookupAsked, AccountLookupAnswered>,
    INeuron<InflightCrmUnblock>
{
    public Task<AccountLookupAnswered?> HandleAsync(
        AccountLookupAsked question, CancellationToken cancellationToken)
    {
        State.PendingMessageId = question.MessageId;
        State.PendingDomain = question.Domain;
        return Task.FromResult<AccountLookupAnswered?>(null);
    }

    public Task HandleAsync(InflightCrmUnblock fact, CancellationToken cancellationToken)
    {
        if (State.PendingMessageId is null
            || !string.Equals(State.PendingMessageId, fact.MessageId, StringComparison.Ordinal)
            || State.PendingDomain is null)
        {
            return Task.CompletedTask;
        }

        Emit(new AccountLookupAnswered(
            State.PendingMessageId,
            AccountName: $"acct-{State.PendingDomain}"));
        State.PendingMessageId = null;
        State.PendingDomain = null;
        return Task.CompletedTask;
    }
}

public sealed class InflightSlowCrmState
{
    public string? PendingMessageId { get; set; }
    public string? PendingDomain { get; set; }
}

public sealed class InflightTaskLedger : Neuron,
    INeuron<InflightTaskCreated>,
    INeuron<BehaviorGenerationDrained>,
    INeuron<BehaviorSuperseded>
{
    public Task HandleAsync(InflightTaskCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BehaviorGenerationDrained fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BehaviorSuperseded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Catalog registration so session can journal BehaviorSuperseded as ambient (needs a listener).
public sealed class InflightSupersedeSink : Neuron, INeuron<BehaviorSuperseded>
{
    public Task HandleAsync(BehaviorSuperseded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
