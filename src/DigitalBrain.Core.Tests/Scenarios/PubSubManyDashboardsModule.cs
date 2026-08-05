namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record RaiseIncident(string IncidentId, string Title) : Synapse;

public sealed record IncidentOpened(string IncidentId, string Title) : Synapse;

public sealed record DashboardPaneRefreshed(string Pane, string IncidentId) : Synapse;

// Source names nobody: emit ambient IncidentOpened for all declared dashboard listeners.
public sealed class IncidentDesk : Neuron, INeuron<RaiseIncident>
{
    public Task HandleAsync(RaiseIncident fact, CancellationToken cancellationToken)
    {
        Emit(new IncidentOpened(fact.IncidentId, fact.Title));
        return Task.CompletedTask;
    }
}

// Distinct kinds at the same locus (context name) — declaration fan-out, no Connect.
public sealed class WallOpsDashboard : Neuron, INeuron<IncidentOpened>
{
    public const string Pane = "wall-ops";

    public Task HandleAsync(IncidentOpened fact, CancellationToken cancellationToken)
    {
        Emit(new DashboardPaneRefreshed(Pane, fact.IncidentId));
        return Task.CompletedTask;
    }
}

public sealed class MobileGlanceDashboard : Neuron, INeuron<IncidentOpened>
{
    public const string Pane = "mobile-glance";

    public Task HandleAsync(IncidentOpened fact, CancellationToken cancellationToken)
    {
        Emit(new DashboardPaneRefreshed(Pane, fact.IncidentId));
        return Task.CompletedTask;
    }
}

// Catalog sink for per-pane refresh ambient emit.
public sealed class DashboardRefreshLedger : Neuron, INeuron<DashboardPaneRefreshed>
{
    public Task HandleAsync(DashboardPaneRefreshed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
