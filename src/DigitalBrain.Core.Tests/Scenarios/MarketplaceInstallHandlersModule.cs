using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1 honest: second module type in Compose increases EmailReceived listeners N+1
// (no dynamic catalog / no silo restart). Install is journaled; both listeners hear traffic.

public sealed record MarketplaceInstallRequested(
    string PackageId,
    string BehaviorKind) : Synapse;

public sealed record MarketplacePackageVerified(string PackageId) : Synapse;

public sealed record MarketplaceBehaviorActivated(
    string PackageId,
    string BehaviorKind,
    IReadOnlyList<string> Listens) : Synapse;

public sealed record TravelDisruptionDetected(
    string MessageId,
    string FlightHint) : Synapse;

public sealed record MarketplaceCapabilitiesChanged(string BehaviorKind, int ListenerCountHint) : Synapse;

// Existing listener (N): always in composition.
public sealed class MarketplaceInboxLedger : Neuron, INeuron<EmailReceived>
{
    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// N+1 marketplace behavior: second INeuron<EmailReceived> kind in Compose.
public sealed class TravelDisruptionAssistant : Neuron, INeuron<EmailReceived>
{
    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        if (!fact.Subject.Contains("flight", StringComparison.OrdinalIgnoreCase)
            && !fact.Snippet.Contains("delayed", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        Emit(new TravelDisruptionDetected(fact.MessageId, FlightHint: fact.Subject));
        return Task.CompletedTask;
    }
}

// Install pipeline journals activation; does not hot-load types (already in composition).
public sealed class MarketplaceInstaller : Neuron, INeuron<MarketplaceInstallRequested>
{
    public Task HandleAsync(MarketplaceInstallRequested fact, CancellationToken cancellationToken)
    {
        Emit(new MarketplacePackageVerified(fact.PackageId));
        Emit(new MarketplaceBehaviorActivated(
            fact.PackageId,
            fact.BehaviorKind,
            Listens: ["emailreceived"]));
        Emit(new MarketplaceCapabilitiesChanged(fact.BehaviorKind, ListenerCountHint: 2));
        return Task.CompletedTask;
    }
}

public sealed class MarketplaceTopologyLedger : Neuron,
    INeuron<MarketplacePackageVerified>,
    INeuron<MarketplaceBehaviorActivated>,
    INeuron<MarketplaceCapabilitiesChanged>,
    INeuron<TravelDisruptionDetected>
{
    public Task HandleAsync(MarketplacePackageVerified fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(MarketplaceBehaviorActivated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(MarketplaceCapabilitiesChanged fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(TravelDisruptionDetected fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
