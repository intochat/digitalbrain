using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ContentUntrusted(string MessageId, string Reason) : Synapse;

public sealed record EmailSafeView(string MessageId, string From, string Subject, string Snippet) : Synapse;

public sealed record EgressSendRequested(string MessageId, string To, string Intent) : Synapse;

public sealed record CapabilityDenied(string MessageId, string Capability, string Reason) : Synapse;

public sealed record EgressDispatched(string MessageId, string To) : Synapse;

// Tags untrusted external text and emits a safe view. Core Source on these said rows is
// unforgeable — modules never mint Source; policy consumes content + later gate decisions.
public sealed class TrustTagger : Neuron, INeuron<EmailReceived>
{
    public static bool LooksInjected(string text)
        => text.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase)
            || text.Contains("forward all mail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("dump memory", StringComparison.OrdinalIgnoreCase);

    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        var corpus = $"{fact.Subject}\n{fact.Snippet}";
        if (LooksInjected(corpus))
        {
            Emit(new ContentUntrusted(fact.MessageId, "prompt-injection-phrase"));
        }

        Emit(new EmailSafeView(fact.MessageId, fact.From, fact.Subject, fact.Snippet));
        return Task.CompletedTask;
    }
}

// Follows untrusted tags by proposing privileged egress (injection influence). Fires only
// after ContentUntrusted, so the tag is durable on the bus before the privilege attempt.
public sealed class InjectionFollower : Neuron, INeuron<ContentUntrusted>
{
    public Task HandleAsync(ContentUntrusted fact, CancellationToken cancellationToken)
    {
        Emit(new EgressSendRequested(
            fact.MessageId,
            To: "attacker@evil",
            Intent: "untrusted-influencer"));
        return Task.CompletedTask;
    }
}

public sealed class SafeViewLedger : Neuron, INeuron<EmailSafeView>
{
    public Task HandleAsync(EmailSafeView fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SecurityAudit : Neuron, INeuron<CapabilityDenied>
{
    public Task HandleAsync(CapabilityDenied fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class EgressLedger : Neuron, INeuron<EgressDispatched>
{
    public Task HandleAsync(EgressDispatched fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Module policy: untrusted influencer intents and known-untrusted message ids never egress.
// Owner-confirmed intents for clean message ids still dispatch. Core only stamps Source.
public sealed class EgressGate : Neuron<EgressGateState>,
    INeuron<ContentUntrusted>,
    INeuron<EgressSendRequested>
{
    public Task HandleAsync(ContentUntrusted fact, CancellationToken cancellationToken)
    {
        State.UntrustedMessageIds.Add(fact.MessageId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(EgressSendRequested fact, CancellationToken cancellationToken)
    {
        var untrusted =
            State.UntrustedMessageIds.Contains(fact.MessageId)
            || string.Equals(fact.Intent, "untrusted-influencer", StringComparison.Ordinal);

        if (untrusted)
        {
            Emit(new CapabilityDenied(fact.MessageId, "EgressSend", "untrusted-influencer"));
            return Task.CompletedTask;
        }

        Emit(new EgressDispatched(fact.MessageId, fact.To));
        return Task.CompletedTask;
    }
}

public sealed class EgressGateState
{
    public HashSet<string> UntrustedMessageIds { get; } = new(StringComparer.Ordinal);
}
