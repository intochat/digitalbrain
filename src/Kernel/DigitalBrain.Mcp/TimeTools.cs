using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Time;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class TimeTools(IDigitalBrain brain)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = McpSurface.ArmSchedule)]
    [Description(
        "Arm a recurring schedule (Wave 5). periodSeconds is the cadence; "
        + "use 5 for a live catch-up gate (20s downtime → CollapsedPeriods=4).")]
    public async Task<string> ArmScheduleAsync(
        [Description("Schedule instance name, e.g. board-refresh")] string name,
        [Description("Period in seconds")] int periodSeconds = 300,
        [Description("Note carried on each tick")] string note = "tick",
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (periodSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        }

        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));

        var instance = PrincipalPartition.InstanceName(principal, name.Trim());
        var actor = new ActorContext(principal, username);
        var armed = await brain
            .Get<ISchedule>(instance)
            .FireAsync<ScheduleArmed>(
                new ArmSchedule(CommandId.New(), periodSeconds, note, actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"ARMED schedule={armed.Schedule} generation={armed.Generation} "
            + $"period={armed.Period.TotalSeconds}s nextDue={armed.NextDue:o} note={armed.Note}";
    }

    [McpServerTool(Name = McpSurface.ForceScheduleCatchUp)]
    [Description(
        "Force phase-preserving catch-up as if the silo missed N periods "
        + "(same math as downtime; default N=4 → Resolution=Recovered, CollapsedPeriods=4).")]
    public async Task<string> ForceScheduleCatchUpAsync(
        [Description("Schedule local name")] string name,
        [Description("Missed periods to collapse")] int missedPeriods = 4,
        [Description("Principal key")] string principalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));

        var instance = PrincipalPartition.InstanceName(principal, name.Trim());
        var tick = await brain
            .Get<ISchedule>(instance)
            .FireAsync<ScheduleTick>(
                new ForceScheduleCatchUp(CommandId.New(), missedPeriods),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"TICK Resolution={tick.Resolution} CollapsedPeriods={tick.CollapsedPeriods} "
            + $"dueAt={tick.DueAt:o} observedAt={tick.ObservedAt:o} nextDue={tick.NextDue:o} note={tick.Note}";
    }

    [McpServerTool(Name = McpSurface.ReadSchedule)]
    [Description("Read schedule snapshot including last catch-up Resolution/CollapsedPeriods.")]
    public async Task<string> ReadScheduleAsync(
        [Description("Schedule local name")] string name,
        [Description("Principal key")] string principalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));

        var instance = PrincipalPartition.InstanceName(principal, name.Trim());
        var snap = await brain
            .GetGrainProxy<ISchedule>(instance)
            .Read()
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"status={snap.Status} generation={snap.Generation} "
            + $"period={snap.Period?.TotalSeconds}s nextDue={snap.NextDue:o} "
            + $"lastTick={snap.LastTickAt:o} lastResolution={snap.LastResolution} "
            + $"lastCollapsedPeriods={snap.LastCollapsedPeriods} note={snap.Note}";
    }

    [McpServerTool(Name = McpSurface.ReadCorpus)]
    [Description("Read the corpus watermark page (Wave 5 memory projection).")]
    public async Task<string> ReadCorpusAsync(
        [Description("Read entries after this sequence")] long afterSequence = 0,
        [Description("Max entries")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Bound);

        var page = await brain
            .Get<ICorpus>(ICorpus.InstanceName)
            .FireAsync<CorpusPage>(
                new ReadCorpus(CommandId.New(), afterSequence, limit),
                timeout.Token)
            .ConfigureAwait(false);

        var lines = page.Entries.Select(e =>
            $"#{e.Sequence} [{e.Kind}] {e.Text} @ {e.At:o}");
        return $"watermark={page.Watermark} truncated={page.Truncated}\n"
            + string.Join("\n", lines);
    }

}
