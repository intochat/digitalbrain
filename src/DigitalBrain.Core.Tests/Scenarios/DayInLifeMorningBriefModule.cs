namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MorningBriefRequested(string BriefId) : Synapse;

public sealed record WeatherReady(string BriefId, string Summary) : Synapse;

public sealed record CalendarReady(string BriefId, string Summary) : Synapse;

public sealed record InboxReady(string BriefId, string Summary) : Synapse;

public sealed record PortfolioReady(string BriefId, string Summary) : Synapse;

public sealed record MorningBriefReady(
    string BriefId,
    string Weather,
    string Calendar,
    string Inbox,
    string Portfolio) : Synapse;

public sealed class MorningBriefState
{
    public string? BriefId { get; set; }
    public string? Weather { get; set; }
    public string? Calendar { get; set; }
    public string? Inbox { get; set; }
    public string? Portfolio { get; set; }
    public bool Completed { get; set; }
}

// Fan-in join in durable TState: MorningBriefReady only when all four section legs are present.
public sealed class MorningBriefAssembler : Neuron<MorningBriefState>,
    INeuron<MorningBriefRequested>,
    INeuron<WeatherReady>,
    INeuron<CalendarReady>,
    INeuron<InboxReady>,
    INeuron<PortfolioReady>
{
    public Task HandleAsync(MorningBriefRequested fact, CancellationToken cancellationToken)
    {
        State.BriefId = fact.BriefId;
        State.Weather = null;
        State.Calendar = null;
        State.Inbox = null;
        State.Portfolio = null;
        State.Completed = false;
        return Task.CompletedTask;
    }

    public Task HandleAsync(WeatherReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Weather = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(CalendarReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Calendar = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(InboxReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Inbox = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(PortfolioReady fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.BriefId))
        {
            return Task.CompletedTask;
        }

        State.Portfolio = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    private bool Matches(string briefId)
        => string.Equals(State.BriefId, briefId, StringComparison.Ordinal);

    private void TryComplete()
    {
        if (State.Completed
            || State.BriefId is null
            || State.Weather is null
            || State.Calendar is null
            || State.Inbox is null
            || State.Portfolio is null)
        {
            return;
        }

        State.Completed = true;
        Emit(new MorningBriefReady(
            State.BriefId,
            State.Weather,
            State.Calendar,
            State.Inbox,
            State.Portfolio));
    }
}

public sealed class WeatherMock : Neuron, INeuron<MorningBriefRequested>
{
    public Task HandleAsync(MorningBriefRequested fact, CancellationToken cancellationToken)
    {
        Emit(new WeatherReady(fact.BriefId, "Clear, 18C"));
        return Task.CompletedTask;
    }
}

public sealed class CalendarMock : Neuron, INeuron<MorningBriefRequested>
{
    public Task HandleAsync(MorningBriefRequested fact, CancellationToken cancellationToken)
    {
        Emit(new CalendarReady(fact.BriefId, "09:00 standup; 14:00 design review"));
        return Task.CompletedTask;
    }
}

public sealed class InboxMock : Neuron, INeuron<MorningBriefRequested>
{
    public Task HandleAsync(MorningBriefRequested fact, CancellationToken cancellationToken)
    {
        Emit(new InboxReady(fact.BriefId, "2 VIP threads"));
        return Task.CompletedTask;
    }
}

public sealed class PortfolioMock : Neuron, INeuron<MorningBriefRequested>
{
    public Task HandleAsync(MorningBriefRequested fact, CancellationToken cancellationToken)
    {
        Emit(new PortfolioReady(fact.BriefId, "BTC +1.2%; cash steady"));
        return Task.CompletedTask;
    }
}

// Catalog sink for terminal ambient MorningBriefReady.
public sealed class MorningBriefLedger : Neuron, INeuron<MorningBriefReady>
{
    public Task HandleAsync(MorningBriefReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
