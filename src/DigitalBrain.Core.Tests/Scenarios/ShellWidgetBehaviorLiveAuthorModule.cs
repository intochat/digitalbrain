namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record WidgetBehaviorInstallProposed(
    string BehaviorId,
    string WidgetId,
    string TitlePattern) : Synapse;

public sealed record WidgetBehaviorActivated(
    string BehaviorId,
    string WidgetId,
    string TitlePattern) : Synapse;

public sealed record WidgetBound(
    string WidgetId,
    string BehaviorId) : Synapse;

public sealed record BoardCalendarEventCreated(
    string EventId,
    string Title,
    string StartsAt) : Synapse;

public sealed record WidgetPropsPatched(
    string WidgetId,
    string Title,
    string Remaining,
    string Urgency) : Synapse;

public sealed record WidgetRendered(
    string WidgetId,
    string Title,
    string Remaining) : Synapse;

// Stage-1 catalog: install proposal → activated fact (not ALC hot-load).
public sealed class WidgetBehaviorCatalog : Neuron, INeuron<WidgetBehaviorInstallProposed>
{
    public Task HandleAsync(WidgetBehaviorInstallProposed fact, CancellationToken cancellationToken)
    {
        Emit(new WidgetBehaviorActivated(fact.BehaviorId, fact.WidgetId, fact.TitlePattern));
        Emit(new WidgetBound(fact.WidgetId, fact.BehaviorId));
        return Task.CompletedTask;
    }
}

// User-authored binder: CalendarEventCreated matching title pattern → WidgetPropsPatched.
public sealed class BoardCountdownBinder : Neuron<BoardCountdownState>,
    INeuron<WidgetBehaviorActivated>,
    INeuron<BoardCalendarEventCreated>
{
    public const string BehaviorId = "board-countdown-binder";

    public Task HandleAsync(WidgetBehaviorActivated fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.BehaviorId, BehaviorId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.WidgetId = fact.WidgetId;
        State.TitlePattern = fact.TitlePattern;
        State.Active = true;
        return Task.CompletedTask;
    }

    public Task HandleAsync(BoardCalendarEventCreated fact, CancellationToken cancellationToken)
    {
        if (!State.Active
            || State.WidgetId is null
            || State.TitlePattern is null
            || !fact.Title.Contains(State.TitlePattern, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        Emit(new WidgetPropsPatched(
            State.WidgetId,
            Title: fact.Title,
            Remaining: $"until {fact.StartsAt}",
            Urgency: "high"));
        return Task.CompletedTask;
    }
}

public sealed class BoardCountdownState
{
    public string? WidgetId { get; set; }
    public string? TitlePattern { get; set; }
    public bool Active { get; set; }
}

// Shell widget host: props patch → rendered projection.
public sealed class ShellWidgetHost : Neuron, INeuron<WidgetPropsPatched>, INeuron<WidgetBound>
{
    public Task HandleAsync(WidgetBound fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WidgetPropsPatched fact, CancellationToken cancellationToken)
    {
        Emit(new WidgetRendered(fact.WidgetId, fact.Title, fact.Remaining));
        return Task.CompletedTask;
    }
}

// Catalog sinks for activation / bind / render ambient facts.
public sealed class WidgetAuthorLedger : Neuron,
    INeuron<WidgetBehaviorActivated>,
    INeuron<WidgetBound>,
    INeuron<WidgetPropsPatched>,
    INeuron<WidgetRendered>
{
    public Task HandleAsync(WidgetBehaviorActivated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WidgetBound fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WidgetPropsPatched fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WidgetRendered fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
