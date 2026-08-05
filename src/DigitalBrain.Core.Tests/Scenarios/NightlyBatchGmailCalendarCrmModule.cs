namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ArmNightlyReconcile(string DayKey, TimeSpan DueIn) : Synapse;

public sealed record NightlyReconcileDue(string DayKey) : Synapse;

public sealed record NightlyGmailSectionAsked(string DayKey) : Synapse;

public sealed record NightlyCalendarSectionAsked(string DayKey) : Synapse;

public sealed record NightlyCrmSectionAsked(string DayKey) : Synapse;

public sealed record NightlyGmailSection(string DayKey, string Summary) : Synapse;

public sealed record NightlyCalendarSection(string DayKey, string Summary) : Synapse;

public sealed record NightlyCrmSection(string DayKey, string Summary) : Synapse;

public sealed record NightlyMorningPackReady(
    string DayKey,
    string Gmail,
    string Calendar,
    string Crm) : Synapse;

public sealed class NightlyReconcileState
{
    public string? DayKey { get; set; }
    public string? Gmail { get; set; }
    public string? Calendar { get; set; }
    public string? Crm { get; set; }
    public bool Completed { get; set; }
}

// Schedule → due tick → fan-out section asks → join in TState → Morning pack.
public sealed class NightlyReconcile : Neuron<NightlyReconcileState>,
    INeuron<ArmNightlyReconcile>,
    INeuron<NightlyReconcileDue>,
    INeuron<NightlyGmailSection>,
    INeuron<NightlyCalendarSection>,
    INeuron<NightlyCrmSection>
{
    public Task HandleAsync(ArmNightlyReconcile fact, CancellationToken cancellationToken)
    {
        State.DayKey = fact.DayKey;
        State.Gmail = null;
        State.Calendar = null;
        State.Crm = null;
        State.Completed = false;
        Schedule(new NightlyReconcileDue(fact.DayKey), fact.DueIn);
        return Task.CompletedTask;
    }

    public Task HandleAsync(NightlyReconcileDue fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.DayKey, fact.DayKey, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Ask<NightlyGmailSection>(new NightlyGmailSectionAsked(fact.DayKey));
        // Sequential asks: one open pin per kind is fine; three different question types.
        Ask<NightlyCalendarSection>(new NightlyCalendarSectionAsked(fact.DayKey));
        Ask<NightlyCrmSection>(new NightlyCrmSectionAsked(fact.DayKey));
        Unschedule<NightlyReconcileDue>();
        return Task.CompletedTask;
    }

    public Task HandleAsync(NightlyGmailSection fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.DayKey))
        {
            return Task.CompletedTask;
        }

        State.Gmail = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(NightlyCalendarSection fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.DayKey))
        {
            return Task.CompletedTask;
        }

        State.Calendar = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    public Task HandleAsync(NightlyCrmSection fact, CancellationToken cancellationToken)
    {
        if (!Matches(fact.DayKey))
        {
            return Task.CompletedTask;
        }

        State.Crm = fact.Summary;
        TryComplete();
        return Task.CompletedTask;
    }

    private bool Matches(string dayKey)
        => string.Equals(State.DayKey, dayKey, StringComparison.Ordinal);

    private void TryComplete()
    {
        if (State.Completed
            || State.DayKey is null
            || State.Gmail is null
            || State.Calendar is null
            || State.Crm is null)
        {
            return;
        }

        State.Completed = true;
        Emit(new NightlyMorningPackReady(State.DayKey, State.Gmail, State.Calendar, State.Crm));
    }
}

public sealed class NightlyGmailSource : Neuron, IAnswers<NightlyGmailSectionAsked, NightlyGmailSection>
{
    public Task<NightlyGmailSection?> HandleAsync(
        NightlyGmailSectionAsked question, CancellationToken cancellationToken)
        => Task.FromResult<NightlyGmailSection?>(
            new NightlyGmailSection(question.DayKey, "3 unanswered VIP threads"));
}

public sealed class NightlyCalendarSource : Neuron, IAnswers<NightlyCalendarSectionAsked, NightlyCalendarSection>
{
    public Task<NightlyCalendarSection?> HandleAsync(
        NightlyCalendarSectionAsked question, CancellationToken cancellationToken)
        => Task.FromResult<NightlyCalendarSection?>(
            new NightlyCalendarSection(question.DayKey, "2 gaps before 10:00"));
}

public sealed class NightlyCrmSource : Neuron, IAnswers<NightlyCrmSectionAsked, NightlyCrmSection>
{
    public Task<NightlyCrmSection?> HandleAsync(
        NightlyCrmSectionAsked question, CancellationToken cancellationToken)
        => Task.FromResult<NightlyCrmSection?>(
            new NightlyCrmSection(question.DayKey, "1 opp missing next step"));
}

public sealed class NightlyPackLedger : Neuron, INeuron<NightlyMorningPackReady>
{
    public Task HandleAsync(NightlyMorningPackReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
