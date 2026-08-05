namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record StandupBriefDue(string BriefId, string TeamId, string Range) : Synapse;

public sealed record StandupTasksReady(string BriefId, string Summary) : Synapse;

public sealed record StandupBlockersReady(string BriefId, string Summary) : Synapse;

public sealed record StandupCalendarReady(string BriefId, string Summary) : Synapse;

public sealed record StandupDealsReady(string BriefId, string Summary) : Synapse;

public sealed record StandupBriefBuilt(
    string BriefId,
    string TeamId,
    string Yesterday,
    string Blockers,
    string TodayPlan,
    string Deals) : Synapse;

public sealed class StandupAssemblerState
{
    public string? BriefId { get; set; }
    public string? TeamId { get; set; }
    public string? Tasks { get; set; }
    public string? Blockers { get; set; }
    public string? Calendar { get; set; }
    public string? Deals { get; set; }
    public bool Completed { get; set; }
}

// TState fan-in join: StandupBriefBuilt only when all four section legs are present.
public sealed class StandupBriefAssembler : Neuron<StandupAssemblerState>,
    INeuron<StandupBriefDue>,
    INeuron<StandupTasksReady>,
    INeuron<StandupBlockersReady>,
    INeuron<StandupCalendarReady>,
    INeuron<StandupDealsReady>
{
    public Task HandleAsync(StandupBriefDue fact, CancellationToken cancellationToken)
    {
        State.BriefId = fact.BriefId;
        State.TeamId = fact.TeamId;
        State.Tasks = null;
        State.Blockers = null;
        State.Calendar = null;
        State.Deals = null;
        State.Completed = false;
        return Task.CompletedTask;
    }

    public Task HandleAsync(StandupTasksReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Tasks = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StandupBlockersReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Blockers = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StandupCalendarReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Calendar = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(StandupDealsReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Deals = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    private bool Matches(string briefId)
        => string.Equals(State.BriefId, briefId, StringComparison.Ordinal);

    private void TryComplete()
    {
        if (State.Completed
            || State.BriefId is null
            || State.TeamId is null
            || State.Tasks is null
            || State.Blockers is null
            || State.Calendar is null
            || State.Deals is null)
        {
            return;
        }

        State.Completed = true;
        Emit(new StandupBriefBuilt(
            State.BriefId,
            State.TeamId,
            Yesterday: State.Tasks,
            Blockers: State.Blockers,
            TodayPlan: State.Calendar,
            Deals: State.Deals));
    }
}

public sealed class StandupTasksSource : Neuron, INeuron<StandupBriefDue>
{
    public Task HandleAsync(StandupBriefDue fact, CancellationToken cancellationToken)
    {
        Emit(new StandupTasksReady(fact.BriefId, "Closed: ship notes; open: flaky test"));
        return Task.CompletedTask;
    }
}

public sealed class StandupBlockersSource : Neuron, INeuron<StandupBriefDue>
{
    public Task HandleAsync(StandupBriefDue fact, CancellationToken cancellationToken)
    {
        Emit(new StandupBlockersReady(fact.BriefId, "Waiting on legal review for Acme MSA"));
        return Task.CompletedTask;
    }
}

public sealed class StandupCalendarSource : Neuron, INeuron<StandupBriefDue>
{
    public Task HandleAsync(StandupBriefDue fact, CancellationToken cancellationToken)
    {
        Emit(new StandupCalendarReady(fact.BriefId, "09:05 standup; 11:00 design"));
        return Task.CompletedTask;
    }
}

public sealed class StandupDealsSource : Neuron, INeuron<StandupBriefDue>
{
    public Task HandleAsync(StandupBriefDue fact, CancellationToken cancellationToken)
    {
        Emit(new StandupDealsReady(fact.BriefId, "Northwind +$40k stage; Fabrikam stalled"));
        return Task.CompletedTask;
    }
}

// Catalog sink for terminal standup brief.
public sealed class StandupBriefLedger : Neuron, INeuron<StandupBriefBuilt>
{
    public Task HandleAsync(StandupBriefBuilt fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
