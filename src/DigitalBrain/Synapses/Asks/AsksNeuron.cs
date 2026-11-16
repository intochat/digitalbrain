using System.Collections.Frozen;
using DigitalBrain.Neurons;

namespace DigitalBrain.Units;

[Neuron(AskRules.AsksNeuronType)]
internal sealed class AsksNeuron(ISynapseCatalogSource catalog, IUnitResolver? units = null, AskProgressHub? progress = null) : Neuron, IAsks
{
    internal const string DueTimer = "asks-due";
    internal const string NudgeReminder = "asks-nudge";
    internal static readonly TimeSpan Nudge = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    private static readonly IReadOnlySet<string> EmptyKeys = FrozenSet<string>.Empty;
    private readonly IUnitResolver? _units = units;
    private readonly AskProgressHub? _progress = progress;
    private AsksState _state = new();

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _state = await Seat.Load<AsksState>().ConfigureAwait(true) ?? new AsksState();
        if (_state.Batches.Count > 0)
        {
            await Context.Reminders.Start(NudgeReminder, Nudge, Nudge).ConfigureAwait(true);
            await Sweep().ConfigureAwait(true);
            await Pump().ConfigureAwait(true);
            await Flush().ConfigureAwait(true);
        }
        else if (_state.Unsent.Count > 0)
        {
            await Flush().ConfigureAwait(true);
        }
    }

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!string.Equals(name, DueTimer, StringComparison.Ordinal)
            && !string.Equals(name, NudgeReminder, StringComparison.Ordinal))
        {
            return;
        }

        await Sweep().ConfigureAwait(true);
        await Pump().ConfigureAwait(true);
        await Flush().ConfigureAwait(true);
    }

    [On]
    public async Task OnAsked(Asked asked, CancellationToken ct)
    {
        _ = ct;
        if (asked is null
            || !string.Equals(asked.Unit, Id.Key, StringComparison.Ordinal)
            || string.IsNullOrEmpty(asked.Batch)
            || asked.Asks is not { Count: > 0 }
            || _state.ContainsBatch(asked.Batch))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(catalog);
        var descriptors = catalog.Static.For(Id.Key);
        if (descriptors.Count == 0 && _units is not null)
        {
            using var handle = await _units.Resolve(Id.Brain, Id.Key, UnitView.Live, ct).ConfigureAwait(true);
            if (handle is not null)
            {
                descriptors = handle.Catalog.For(Id.Key);
            }
        }

        try
        {
            _ = AskRules.Checked(asked.Asks, descriptors);
        }
        catch (InvalidOperationException) when (_units is not null)
        {
            using var handle = await _units.Resolve(Id.Brain, Id.Key, UnitView.Live, ct).ConfigureAwait(true);
            if (handle is null)
            {
                return;
            }

            try
            {
                _ = AskRules.Checked(asked.Asks, handle.Catalog.For(Id.Key));
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var now = Context.Time.GetUtcNow();
        if (_state.IsTrippedAt(now))
        {
            return;
        }

        var openKeys = _state.Batches.Count > 0 && _state.HasBudgetFor(asked.Asks.Count, now)
            ? await CollectOpenKeys(_state.Batches).ConfigureAwait(true)
            : EmptyKeys;

        var outcome = _state.Accept(asked.Batch, asked.Asks, openKeys, now);
        if (!outcome.Save)
        {
            return;
        }

        if (outcome.Accepted.Count > 0)
        {
            await Get<IAskBatch>(AskRules.BatchOf(Id, asked.Batch)).Put(Id.Key, outcome.Accepted).ConfigureAwait(true);
            await Context.Reminders.Start(NudgeReminder, Nudge, Nudge).ConfigureAwait(true);
        }

        await Save(outcome.State, []).ConfigureAwait(true);
        await Pump().ConfigureAwait(true);
        await Flush(now: outcome.CreatedAnswers).ConfigureAwait(true);
    }

    public Task Attach(CancellationToken ct) =>
        Context.Synapses.Ensure<Asked>(new NeuronId(Id.Brain, Id.Key, "default"));

    public async Task Detach(CancellationToken ct)
    {
        _ = ct;
        var batches = _state.Batches;
        for (var i = 0; i < batches.Count; i++)
        {
            await Get<IAskBatch>(AskRules.BatchOf(Id, batches[i].Id)).Drop().ConfigureAwait(true);
        }

        await Context.Synapses.Remove(new NeuronId(Id.Brain, Id.Key, "default")).ConfigureAwait(true);
        Context.Timers.Stop(DueTimer);
        await Context.Reminders.Stop(NudgeReminder).ConfigureAwait(true);
        await Save(new AsksState(), []).ConfigureAwait(true);
    }

    public async Task Renew(AskLeaseRenewal renewal)
    {
        if (renewal is null || string.IsNullOrEmpty(renewal.Batch))
        {
            return;
        }

        if (Context.Caller is { } caller && caller.Via != AskRules.BatchOf(Id, renewal.Batch))
        {
            throw new UnauthorizedAccessException("Only the batch neuron of this ask may renew its lease.");
        }

        var now = Context.Time.GetUtcNow();
        var outcome = _state.ExtendLease(renewal.Batch, renewal.Index, renewal.Attempt, renewal.Extension, now, Grace);
        if (!outcome.Matched)
        {
            return;
        }

        if (outcome.Changed)
        {
            await Save(outcome.State, []).ConfigureAwait(true);
        }

        ScheduleDue(now);
    }

    public async Task ReportProgress(AskProgressReport report)
    {
        if (report is null || string.IsNullOrEmpty(report.Batch))
        {
            return;
        }

        if (Context.Caller is { } caller && caller.Via != AskRules.BatchOf(Id, report.Batch))
        {
            throw new UnauthorizedAccessException("Only the batch neuron of this ask may report its progress.");
        }

        var now = Context.Time.GetUtcNow();
        var outcome = _state.ExtendLease(report.Batch, report.Index, report.Attempt, report.LeaseExtension, now, Grace);
        if (!outcome.Matched)
        {
            return;
        }

        // Persist the extended due deadline only once at least half of the extension window has elapsed so frequent ticks stay write-free.
        if (outcome.Changed && outcome.PreviousRemaining <= outcome.EffectiveWindow / 2)
        {
            await Save(outcome.State, []).ConfigureAwait(true);
            ScheduleDue(now);
        }

        var progressed = new AskProgressed(Id.Key, report.RunId, report.Key, report.Status, report.Fraction);
        if (_progress is not null && !_progress.Dispatch(progressed))
        {
            return;
        }

        await PublishEphemeral(progressed).ConfigureAwait(true);
    }

    public async Task Settled(AskSettlement settlement)
    {
        if (settlement is null || string.IsNullOrEmpty(settlement.Batch))
        {
            return;
        }

        if (Context.Caller is { } caller && caller.Via != AskRules.BatchOf(Id, settlement.Batch))
        {
            throw new UnauthorizedAccessException("Only the batch neuron of this ask may settle it.");
        }

        var outcome = _state.Settle(settlement, Context.Time.GetUtcNow());
        if (!outcome.Changed)
        {
            return;
        }

        await Save(outcome.State, []).ConfigureAwait(true);
        if (outcome.DroppedBatchId is { } droppedBatchId)
        {
            await Get<IAskBatch>(AskRules.BatchOf(Id, droppedBatchId)).Drop().ConfigureAwait(true);
        }

        await Pump().ConfigureAwait(true);
        await Flush().ConfigureAwait(true);
    }

    public Task Wake() => Task.CompletedTask;

    private async Task Sweep()
    {
        var now = Context.Time.GetUtcNow();
        var needed = _state.BatchesNeedingAsks(now);
        if (needed.Count == 0)
        {
            return;
        }

        var storedAsksByBatch = new Dictionary<string, IReadOnlyList<Ask>>(needed.Count, StringComparer.Ordinal);
        for (var i = 0; i < needed.Count; i++)
        {
            var batchId = needed[i];
            storedAsksByBatch[batchId] = await Get<IAskBatch>(AskRules.BatchOf(Id, batchId)).All().ConfigureAwait(true);
        }

        var outcome = _state.Sweep(storedAsksByBatch, now);
        if (!outcome.Changed)
        {
            return;
        }

        await Save(outcome.State, []).ConfigureAwait(true);
        for (var i = 0; i < outcome.DroppedBatchIds.Count; i++)
        {
            await Get<IAskBatch>(AskRules.BatchOf(Id, outcome.DroppedBatchIds[i])).Drop().ConfigureAwait(true);
        }
    }

    private async Task Pump()
    {
        var outcome = _state.Start(Context.Time.GetUtcNow(), Grace);
        if (outcome.Started.Count == 0)
        {
            return;
        }

        await Save(outcome.State, []).ConfigureAwait(true);
        for (var i = 0; i < outcome.Started.Count; i++)
        {
            var started = outcome.Started[i];
            await Get<IAskBatch>(AskRules.BatchOf(Id, started.BatchId)).Run(started.Index, started.Attempt).ConfigureAwait(true);
        }
    }

    private async Task Flush(bool now = false)
    {
        while (_state.ShouldFlush(now, Context.Time.GetUtcNow()) && _state.NextFlushGroup() is { } flush)
        {
            await Save(flush.State, [new Answered(Id.Key, flush.Group)]).ConfigureAwait(true);
        }

        if (_state.Batches.Count == 0 && _state.Unsent.Count == 0)
        {
            Context.Timers.Stop(DueTimer);
            await Context.Reminders.Stop(NudgeReminder).ConfigureAwait(true);
        }
        else
        {
            ScheduleDue(Context.Time.GetUtcNow());
        }
    }

    private async Task Save(AsksState next, IReadOnlyList<Signal> signals)
    {
        await Seat.Save(next, signals).ConfigureAwait(true);
        _state = next;
    }

    private void ScheduleDue(DateTimeOffset now)
    {
        if (_state.NextDue() is { } target)
        {
            var due = target > now ? target - now : TimeSpan.Zero;
            Context.Timers.Start(DueTimer, due, period: null, keepAlive: true);
        }
        else
        {
            Context.Timers.Stop(DueTimer);
        }
    }

    private async Task<HashSet<string>> CollectOpenKeys(IReadOnlyList<OpenBatch> batches)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var indexes = AsksState.UnsettledIndexes(batch);
            if (indexes.Count == 0)
            {
                continue;
            }

            var asks = await Get<IAskBatch>(AskRules.BatchOf(Id, batch.Id)).All().ConfigureAwait(true);
            for (var j = 0; j < indexes.Count; j++)
            {
                var index = indexes[j];
                if ((uint)index < (uint)asks.Count)
                {
                    keys.Add(asks[index].Key);
                }
            }
        }

        return keys;
    }
}
