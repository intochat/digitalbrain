using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record LegalHoldPlaceAsked(
    string HoldId,
    string SubjectAccount,
    string Policy) : Synapse;

public sealed record LegalHoldPlaced(
    string HoldId,
    string SubjectAccount,
    string Policy) : Synapse;

public sealed record LegalHoldLifted(string HoldId, string SubjectAccount) : Synapse;

public sealed record DestructiveDeleteAsked(
    string SubjectAccount,
    string Scope,
    string Actor) : Synapse;

public sealed record DestructiveActionBlocked(
    string SubjectAccount,
    string Scope,
    string Reason,
    string HoldId) : Synapse;

public sealed record DestructiveDeleteExecuted(
    string SubjectAccount,
    string Scope) : Synapse;

public sealed record RetentionExtended(
    string MessageId,
    string HoldId) : Synapse;

public sealed class ComplianceHoldState
{
    public string? HoldId { get; set; }
    public string? SubjectAccount { get; set; }
    public bool Active { get; set; }
}

// Authoritative hold register: place/lift journaled; answers destructive gate via local state.
public sealed class ComplianceHoldRegister : Neuron<ComplianceHoldState>,
    INeuron<LegalHoldPlaceAsked>,
    INeuron<LegalHoldLifted>
{
    public Task HandleAsync(LegalHoldPlaceAsked fact, CancellationToken cancellationToken)
    {
        State.HoldId = fact.HoldId;
        State.SubjectAccount = fact.SubjectAccount;
        State.Active = true;
        Emit(new LegalHoldPlaced(fact.HoldId, fact.SubjectAccount, fact.Policy));
        return Task.CompletedTask;
    }

    public Task HandleAsync(LegalHoldLifted fact, CancellationToken cancellationToken)
    {
        if (!State.Active
            || !string.Equals(State.HoldId, fact.HoldId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.Active = false;
        return Task.CompletedTask;
    }
}

// Gmail-side enforcement: hears hold, blocks destructive deletes, retains inbound Contoso mail.
public sealed class HoldAwareMailbox : Neuron<HoldAwareMailboxState>,
    INeuron<LegalHoldPlaced>,
    INeuron<LegalHoldLifted>,
    INeuron<DestructiveDeleteAsked>,
    INeuron<EmailReceived>
{
    public Task HandleAsync(LegalHoldPlaced fact, CancellationToken cancellationToken)
    {
        State.HoldId = fact.HoldId;
        State.SubjectAccount = fact.SubjectAccount;
        State.Active = true;
        return Task.CompletedTask;
    }

    public Task HandleAsync(LegalHoldLifted fact, CancellationToken cancellationToken)
    {
        if (string.Equals(State.HoldId, fact.HoldId, StringComparison.Ordinal))
        {
            State.Active = false;
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(DestructiveDeleteAsked fact, CancellationToken cancellationToken)
    {
        if (State.Active
            && string.Equals(State.SubjectAccount, fact.SubjectAccount, StringComparison.OrdinalIgnoreCase))
        {
            Emit(new DestructiveActionBlocked(
                fact.SubjectAccount,
                fact.Scope,
                Reason: "legal_hold",
                HoldId: State.HoldId ?? "unknown"));
            return Task.CompletedTask;
        }

        Emit(new DestructiveDeleteExecuted(fact.SubjectAccount, fact.Scope));
        return Task.CompletedTask;
    }

    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        if (!State.Active || State.HoldId is null)
        {
            return Task.CompletedTask;
        }

        // Contoso-related inbound still journals; retention extends under hold.
        if (fact.Domain.Contains("contoso", StringComparison.OrdinalIgnoreCase)
            || fact.Subject.Contains("Contoso", StringComparison.OrdinalIgnoreCase))
        {
            Emit(new RetentionExtended(fact.MessageId, State.HoldId));
        }

        return Task.CompletedTask;
    }
}

public sealed class HoldAwareMailboxState
{
    public string? HoldId { get; set; }
    public string? SubjectAccount { get; set; }
    public bool Active { get; set; }
}

// Catalog sinks for hold / block / retention ambient facts.
public sealed class ComplianceAuditLedger : Neuron,
    INeuron<LegalHoldPlaced>,
    INeuron<DestructiveActionBlocked>,
    INeuron<DestructiveDeleteExecuted>,
    INeuron<RetentionExtended>
{
    public Task HandleAsync(LegalHoldPlaced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(DestructiveActionBlocked fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(DestructiveDeleteExecuted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(RetentionExtended fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
