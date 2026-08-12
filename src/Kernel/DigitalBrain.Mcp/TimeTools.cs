using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Time;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class TimeTools(IDigitalBrain brain, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = McpSurface.ArmSchedule)]
    [Description(
        "Arm a recurring schedule in the authenticated caller's partition (Wave 5). "
        + "periodSeconds is the cadence; use 5 for a live catch-up gate.")]
    public async Task<string> ArmScheduleAsync(
        [Description("Schedule local name, e.g. board-refresh")] string name,
        [Description("Period in seconds")] int periodSeconds = 300,
        [Description("Note carried on each tick")] string note = "tick",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (periodSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        }

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var instance = McpActor.Partition(actor, name.Trim());
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var instance = McpActor.Partition(actor, name.Trim());
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var instance = McpActor.Partition(actor, name.Trim());
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
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Bound);

        var corpusInstance = McpActor.Partition(actor, ICorpus.InstanceName);
        var page = await brain
            .Get<ICorpus>(corpusInstance)
            .FireAsync<CorpusPage>(
                new ReadCorpus(CommandId.New(), afterSequence, limit),
                timeout.Token)
            .ConfigureAwait(false);

        var lines = page.Entries.Select(e =>
            $"#{e.Sequence} [{e.Kind}] {e.Text} @ {e.At:o}");
        return $"watermark={page.Watermark} truncated={page.Truncated}\n"
            + string.Join("\n", lines);
    }

    [McpServerTool(Name = McpSurface.CellApply)]
    [Description("Apply a key to a cell kind@instance (Wave 6 calculator).")]
    public async Task<string> CellApplyAsync(
        [Description("Cell identity kind@name, e.g. calculator@desk")] string identity,
        [Description("Key: digit, operator, =, C, CE, BS")] string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var snap = await brain
            .Get<ICell>(identity.Trim())
            .FireAsync<CellSnapshot>(
                new CellApply(CommandId.New(), key.Trim()),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"kind={snap.Kind} instance={snap.Instance} display={snap.Display} "
            + $"value={snap.Value} phase={snap.Phase}";
    }

    [McpServerTool(Name = McpSurface.CellReset)]
    [Description("Reset a cell to fresh kind state.")]
    public async Task<string> CellResetAsync(
        [Description("Cell identity kind@name")] string identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var snap = await brain
            .Get<ICell>(identity.Trim())
            .FireAsync<CellSnapshot>(
                new CellReset(CommandId.New()),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"RESET kind={snap.Kind} instance={snap.Instance} display={snap.Display}";
    }
}
