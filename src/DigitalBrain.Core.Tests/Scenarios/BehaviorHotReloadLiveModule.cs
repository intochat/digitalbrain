namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 live hot-reload: Connect rewiring of classifier instance; not ALC package unload.

public sealed record LiveObserveEmail(string MessageId, string Domain, string Subject) : Synapse;

public sealed record LiveEmailReceived(string MessageId, string Domain, string Subject) : Synapse;

public sealed record LiveEmailClassified(
    string MessageId,
    string Label,
    string ClassifierRev) : Synapse;

public sealed record LiveUiSurface(string MessageId, string CardKind, string ClassifierRev) : Synapse;

public sealed record BehaviorPackageActivated(string Kind, string Version, string ActiveName) : Synapse;

public sealed class LiveMailHub : Neuron, INeuron<LiveObserveEmail>
{
    public Task HandleAsync(LiveObserveEmail fact, CancellationToken cancellationToken)
    {
        Emit(new LiveEmailReceived(fact.MessageId, fact.Domain, fact.Subject));
        return Task.CompletedTask;
    }
}

// Same kind, different Name (v1/v2): VIP domain differs per generation.
public sealed class LivePolicyClassifier : Neuron, INeuron<LiveEmailReceived>
{
    public Task HandleAsync(LiveEmailReceived fact, CancellationToken cancellationToken)
    {
        var rev = Id.Name;
        var vipDomain = string.Equals(rev, "v1", StringComparison.Ordinal)
            ? "board.example"
            : "investors.example";
        var isVip = string.Equals(fact.Domain, vipDomain, StringComparison.OrdinalIgnoreCase);
        var label = isVip ? "vip" : "normal";
        Emit(new LiveEmailClassified(fact.MessageId, label, rev));
        if (isVip)
        {
            Emit(new LiveUiSurface(fact.MessageId, CardKind: $"VipCard-{rev}", ClassifierRev: rev));
        }

        return Task.CompletedTask;
    }
}

public sealed class LiveClassifierLedger : Neuron,
    INeuron<LiveEmailClassified>,
    INeuron<LiveUiSurface>,
    INeuron<BehaviorPackageActivated>
{
    public Task HandleAsync(LiveEmailClassified fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(LiveUiSurface fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BehaviorPackageActivated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
