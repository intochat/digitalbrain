using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record SupportTicketOpened(string AccountId, string TicketId, int Severity) : Synapse;

public sealed record UsageDropped(string AccountId, double DropPct) : Synapse;

public sealed record ChampionSignalObserved(string AccountId, string Signal) : Synapse;

public sealed record ChurnRiskScoreUpdated(
    string AccountId,
    int Score,
    ImmutableArray<string> Factors) : Synapse;

public sealed record ChurnCaseOpened(
    string CaseId,
    string AccountId,
    string Severity,
    int Score) : Synapse;

public sealed record SavePlayProposed(
    string CaseId,
    string AccountId,
    ImmutableArray<string> Options) : Synapse;

public sealed record ChurnAlertSurfaced(string CaseId, string AccountId) : Synapse;

public sealed class ChurnEngineState
{
#pragma warning disable CA1002, CA2227
    public Dictionary<string, int> Scores { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Factors { get; set; } = new(StringComparer.Ordinal);
#pragma warning restore CA1002, CA2227
    public string? OpenCaseAccountId { get; set; }
}

// Ambient multi-signal scorer: threshold opens one case + save play (initiative without user prompt).
public sealed class ChurnEngine : Neuron<ChurnEngineState>,
    INeuron<SupportTicketOpened>,
    INeuron<UsageDropped>,
    INeuron<ChampionSignalObserved>
{
    public const int Threshold = 70;

    public Task HandleAsync(SupportTicketOpened fact, CancellationToken cancellationToken)
        => Score(fact.AccountId, points: fact.Severity >= 3 ? 40 : 15, factor: $"ticket:{fact.TicketId}");

    public Task HandleAsync(UsageDropped fact, CancellationToken cancellationToken)
        => Score(fact.AccountId, points: fact.DropPct >= 0.3 ? 35 : 10, factor: $"usage-drop:{fact.DropPct:0.00}");

    public Task HandleAsync(ChampionSignalObserved fact, CancellationToken cancellationToken)
        => Score(fact.AccountId, points: 40, factor: $"champion:{fact.Signal}");

    private Task Score(string accountId, int points, string factor)
    {
        if (!State.Scores.TryGetValue(accountId, out var score))
        {
            score = 0;
            State.Factors[accountId] = [];
        }

        score = Math.Min(100, score + points);
        State.Scores[accountId] = score;
        State.Factors[accountId].Add(factor);

        var factors = State.Factors[accountId].ToImmutableArray();
        Emit(new ChurnRiskScoreUpdated(accountId, score, factors));

        // One open case per account — journal gate.
        if (score < Threshold || State.OpenCaseAccountId is not null)
        {
            return Task.CompletedTask;
        }

        State.OpenCaseAccountId = accountId;
        var caseId = $"churn-{accountId}";
        var severity = score >= 90 ? "critical" : "high";
        Emit(new ChurnCaseOpened(caseId, accountId, severity, score));
        Emit(new SavePlayProposed(caseId, accountId, ["execEmail", "qbr", "discount", "successPlan"]));
        Emit(new ChurnAlertSurfaced(caseId, accountId));
        return Task.CompletedTask;
    }
}

public sealed class ChurnDeskLedger : Neuron,
    INeuron<ChurnRiskScoreUpdated>,
    INeuron<ChurnCaseOpened>,
    INeuron<SavePlayProposed>,
    INeuron<ChurnAlertSurfaced>
{
    public Task HandleAsync(ChurnRiskScoreUpdated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ChurnCaseOpened fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(SavePlayProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ChurnAlertSurfaced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
