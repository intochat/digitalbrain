using System.Text;
using System.Text.Json;

namespace DigitalBrain;

[Neuron(AskRules.AsksNeuronType)]
internal sealed class AsksNeuron(ISynapseCatalogSource catalog) : Neuron, IAsks
{
    internal const string DueTimer = "asks-due";
    internal const string NudgeReminder = "asks-nudge";
    internal static readonly TimeSpan Nudge = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    private AsksState _state = new();

    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _state = await Context.State.Load<AsksState>().ConfigureAwait(true) ?? new AsksState();
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
            || FindBatchIndex(_state.Batches, asked.Batch) >= 0)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            _ = AskRules.Checked(asked.Asks, catalog.Current.For(Id.Key));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var now = Context.Time.GetUtcNow();
        var hour = now.ToUnixTimeSeconds() / 3600;
        var askedThisHour = _state.AskedThisHour;
        var tripped = _state.Tripped;
        if (hour != _state.Hour)
        {
            askedThisHour = 0;
            tripped = false;
        }

        if (tripped)
        {
            return;
        }

        var n = asked.Asks.Count;
        var open = OpenCount(_state.Batches);
        var batches = _state.Batches;
        var unsent = new List<Answer>(_state.Unsent);
        var unsentSince = _state.UnsentSince;
        var createdAnswers = false;

        if (open + n > AskLimits.MaxOpenAsks || askedThisHour + n > AskLimits.MaxAsksPerHour)
        {
            if (unsent.Count == 0)
            {
                unsentSince = now;
            }

            for (var i = 0; i < n; i++)
            {
                var ask = asked.Asks[i];
                unsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, AnswerStatus.Refused, AskRules.NoResult, "over-budget", open));
            }

            tripped = true;
            createdAnswers = true;
        }
        else
        {
            var openKeys = await CollectOpenKeys(_state.Batches).ConfigureAwait(true);
            var accepted = new List<Ask>(n);
            var refused = new List<Ask>();
            for (var i = 0; i < n; i++)
            {
                var ask = asked.Asks[i];
                if (openKeys.Add(ask.Key))
                {
                    accepted.Add(ask);
                }
                else
                {
                    refused.Add(ask);
                }
            }

            var openAfter = open + accepted.Count;
            if (refused.Count > 0)
            {
                if (unsent.Count == 0)
                {
                    unsentSince = now;
                }

                for (var i = 0; i < refused.Count; i++)
                {
                    var ask = refused[i];
                    unsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, AnswerStatus.Refused, AskRules.NoResult, "already-open", openAfter));
                }

                createdAnswers = true;
            }

            if (accepted.Count > 0)
            {
                await Get<IAskBatch>(AskRules.BatchOf(Id, asked.Batch)).Put(Id.Key, accepted).ConfigureAwait(true);
                batches = [.. batches, new OpenBatch(asked.Batch, accepted.Count, 0, [], [], 0, LongestTimeout(accepted), now)];
                askedThisHour += accepted.Count;
                await Context.Reminders.Start(NudgeReminder, Nudge, Nudge).ConfigureAwait(true);
            }
        }

        await Save(new AsksState(batches, unsent, unsentSince, hour, askedThisHour, tripped), []).ConfigureAwait(true);
        await Pump().ConfigureAwait(true);
        if (createdAnswers)
        {
            await Flush(now: true).ConfigureAwait(true);
        }
        else
        {
            await Flush().ConfigureAwait(true);
        }
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

    public async Task Settled(AskSettlement settlement)
    {
        if (settlement is null || string.IsNullOrEmpty(settlement.Batch))
        {
            return;
        }

        var batchIndex = FindBatchIndex(_state.Batches, settlement.Batch);
        if (batchIndex < 0)
        {
            return;
        }

        var batch = _state.Batches[batchIndex];
        var flyingIndex = FindFlyingIndex(batch.Flying, settlement.Index, settlement.Attempt);
        if (flyingIndex < 0)
        {
            return;
        }

        var now = Context.Time.GetUtcNow();
        var nextFlying = RemoveAt(batch.Flying, flyingIndex);
        var nextBatches = new List<OpenBatch>(_state.Batches.Count);

        if (settlement.Transient && settlement.Repeatable && settlement.Attempt < AskLimits.MaxAttempts)
        {
            var delay = Backoff[Math.Clamp(settlement.Attempt - 1, 0, Backoff.Length - 1)];
            IReadOnlyList<WaitingAsk> nextWaiting = [.. batch.Waiting, new WaitingAsk(settlement.Index, settlement.Attempt + 1, now + delay)];
            for (var i = 0; i < _state.Batches.Count; i++)
            {
                nextBatches.Add(i == batchIndex ? batch with { Flying = nextFlying, Waiting = nextWaiting } : _state.Batches[i]);
            }

            await Save(_state with { Batches = nextBatches }, []).ConfigureAwait(true);
        }
        else
        {
            var nextSettled = batch.Settled + 1;
            var remaining = OpenCount(_state.Batches) - 1;
            var answer = new Answer(settlement.Slot, settlement.Operation, settlement.Key, settlement.Status, settlement.Result, settlement.Error, remaining);
            IReadOnlyList<Answer> nextUnsent = [.. _state.Unsent, answer];
            var nextUnsentSince = _state.Unsent.Count == 0 ? now : _state.UnsentSince ?? now;
            var closed = nextSettled == batch.Count && nextFlying.Count == 0 && batch.Waiting.Count == 0;

            for (var i = 0; i < _state.Batches.Count; i++)
            {
                if (i == batchIndex)
                {
                    if (!closed)
                    {
                        nextBatches.Add(batch with { Flying = nextFlying, Settled = nextSettled });
                    }
                }
                else
                {
                    nextBatches.Add(_state.Batches[i]);
                }
            }

            await Save(_state with { Batches = nextBatches, Unsent = nextUnsent, UnsentSince = nextUnsentSince }, []).ConfigureAwait(true);
            if (closed)
            {
                await Get<IAskBatch>(AskRules.BatchOf(Id, batch.Id)).Drop().ConfigureAwait(true);
            }
        }

        await Pump().ConfigureAwait(true);
        await Flush().ConfigureAwait(true);
    }

    public Task Wake() => Task.CompletedTask;

    private async Task Sweep()
    {
        if (_state.Batches.Count == 0)
        {
            return;
        }

        var now = Context.Time.GetUtcNow();
        var openRemaining = OpenCount(_state.Batches);
        var nextBatches = new List<OpenBatch>(_state.Batches.Count);
        var nextUnsent = new List<Answer>(_state.Unsent);
        var nextUnsentSince = _state.UnsentSince;
        List<string>? droppedBatchIds = null;
        var changed = false;

        for (var i = 0; i < _state.Batches.Count; i++)
        {
            var batch = _state.Batches[i];
            if (batch.AskedAt + AskLimits.MaxAge <= now)
            {
                changed = true;
                var asks = await Get<IAskBatch>(AskRules.BatchOf(Id, batch.Id)).All().ConfigureAwait(true);
                var unsettled = UnsettledIndexes(batch);
                for (var j = 0; j < unsettled.Count; j++)
                {
                    var index = unsettled[j];
                    openRemaining--;
                    if ((uint)index < (uint)asks.Count)
                    {
                        var ask = asks[index];
                        if (nextUnsent.Count == 0)
                        {
                            nextUnsentSince = now;
                        }

                        nextUnsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, AnswerStatus.Expired, AskRules.NoResult, "expired", openRemaining));
                    }
                }

                (droppedBatchIds ??= []).Add(batch.Id);
                continue;
            }

            if (!HasDueFlying(batch.Flying, now))
            {
                nextBatches.Add(batch);
                continue;
            }

            changed = true;
            var storedAsks = await Get<IAskBatch>(AskRules.BatchOf(Id, batch.Id)).All().ConfigureAwait(true);
            var nextFlying = new List<FlyingAsk>(batch.Flying.Count);
            var nextWaiting = new List<WaitingAsk>(batch.Waiting);
            var nextSettled = batch.Settled;

            for (var j = 0; j < batch.Flying.Count; j++)
            {
                var flying = batch.Flying[j];
                if (flying.Due > now)
                {
                    nextFlying.Add(flying);
                    continue;
                }

                if ((uint)flying.Index < (uint)storedAsks.Count)
                {
                    var ask = storedAsks[flying.Index];
                    if (ask.Repeatable && flying.Attempt < AskLimits.MaxAttempts)
                    {
                        var delay = Backoff[Math.Clamp(flying.Attempt - 1, 0, Backoff.Length - 1)];
                        nextWaiting.Add(new WaitingAsk(flying.Index, flying.Attempt + 1, now + delay));
                    }
                    else
                    {
                        nextSettled++;
                        openRemaining--;
                        if (nextUnsent.Count == 0)
                        {
                            nextUnsentSince = now;
                        }

                        var status = ask.Repeatable ? AnswerStatus.Failed : AnswerStatus.Unknown;
                        nextUnsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, status, AskRules.NoResult, "timeout", openRemaining));
                    }
                }
                else
                {
                    nextSettled++;
                    openRemaining--;
                }
            }

            if (nextSettled == batch.Count && nextFlying.Count == 0 && nextWaiting.Count == 0)
            {
                (droppedBatchIds ??= []).Add(batch.Id);
            }
            else
            {
                nextBatches.Add(batch with { Flying = nextFlying, Waiting = nextWaiting, Settled = nextSettled });
            }
        }

        if (changed)
        {
            await Save(_state with { Batches = nextBatches, Unsent = nextUnsent, UnsentSince = nextUnsentSince }, []).ConfigureAwait(true);
            if (droppedBatchIds is not null)
            {
                for (var i = 0; i < droppedBatchIds.Count; i++)
                {
                    await Get<IAskBatch>(AskRules.BatchOf(Id, droppedBatchIds[i])).Drop().ConfigureAwait(true);
                }
            }
        }
    }

    private async Task Pump()
    {
        var now = Context.Time.GetUtcNow();
        var batches = _state.Batches.ToArray();
        var flyingCount = 0;
        for (var i = 0; i < batches.Length; i++)
        {
            flyingCount += batches[i].Flying.Count;
        }

        List<(string BatchId, int Index, int Attempt)>? started = null;
        while (flyingCount < AskLimits.Concurrency)
        {
            var picked = false;
            for (var i = 0; i < batches.Length; i++)
            {
                var batch = batches[i];
                var waitingIndex = ReadyWaitingIndex(batch.Waiting, now);
                if (waitingIndex >= 0)
                {
                    var waiting = batch.Waiting[waitingIndex];
                    var nextWaiting = RemoveAt(batch.Waiting, waitingIndex);
                    IReadOnlyList<FlyingAsk> nextFlying = [.. batch.Flying, new FlyingAsk(waiting.Index, waiting.Attempt, now + batch.Timeout + Grace)];
                    batches[i] = batch with { Flying = nextFlying, Waiting = nextWaiting };
                    (started ??= []).Add((batch.Id, waiting.Index, waiting.Attempt));
                    flyingCount++;
                    picked = true;
                    break;
                }

                if (batch.Next < batch.Count)
                {
                    var index = batch.Next;
                    IReadOnlyList<FlyingAsk> nextFlying = [.. batch.Flying, new FlyingAsk(index, 1, now + batch.Timeout + Grace)];
                    batches[i] = batch with { Next = index + 1, Flying = nextFlying };
                    (started ??= []).Add((batch.Id, index, 1));
                    flyingCount++;
                    picked = true;
                    break;
                }
            }

            if (!picked)
            {
                break;
            }
        }

        if (started is { Count: > 0 })
        {
            await Save(_state with { Batches = batches }, []).ConfigureAwait(true);
            for (var i = 0; i < started.Count; i++)
            {
                var (batchId, index, attempt) = started[i];
                await Get<IAskBatch>(AskRules.BatchOf(Id, batchId)).Run(index, attempt).ConfigureAwait(true);
            }
        }
    }

    private async Task Flush(bool now = false)
    {
        while (_state.Unsent.Count > 0 && ShouldFlush(now, Context.Time.GetUtcNow()))
        {
            var count = GroupSize(_state.Unsent);
            IReadOnlyList<Answer> group = [.. _state.Unsent.Take(count)];
            IReadOnlyList<Answer> remaining = count == _state.Unsent.Count ? [] : [.. _state.Unsent.Skip(count)];
            var next = _state with
            {
                Unsent = remaining,
                UnsentSince = remaining.Count == 0 ? null : _state.UnsentSince,
            };
            await Save(next, [new Answered(Id.Key, group)]).ConfigureAwait(true);
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

    private bool ShouldFlush(bool now, DateTimeOffset clockNow) =>
        now
        || _state.Unsent.Count >= AskLimits.AnswersPerSignal
        || !HasFlyingOrWaiting(_state.Batches)
        || _state.UnsentSince is not { } since
        || clockNow >= since + AskLimits.AnswerFlush;

    private async Task Save(AsksState next, IReadOnlyList<Signal> signals)
    {
        await Context.State.Save(next, signals).ConfigureAwait(true);
        _state = next;
    }

    private void ScheduleDue(DateTimeOffset now)
    {
        var flyingCount = 0;
        for (var i = 0; i < _state.Batches.Count; i++)
        {
            flyingCount += _state.Batches[i].Flying.Count;
        }

        DateTimeOffset? earliest = null;
        for (var i = 0; i < _state.Batches.Count; i++)
        {
            var batch = _state.Batches[i];
            for (var j = 0; j < batch.Flying.Count; j++)
            {
                earliest = Earlier(earliest, batch.Flying[j].Due);
            }

            if (flyingCount < AskLimits.Concurrency)
            {
                for (var j = 0; j < batch.Waiting.Count; j++)
                {
                    earliest = Earlier(earliest, batch.Waiting[j].NotBefore);
                }
            }
        }

        if (_state.Unsent.Count > 0 && _state.UnsentSince is { } unsentSince)
        {
            earliest = Earlier(earliest, unsentSince + AskLimits.AnswerFlush);
        }

        if (earliest is { } target)
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
            if (batch.Next >= batch.Count && batch.Flying.Count == 0 && batch.Waiting.Count == 0)
            {
                continue;
            }

            var asks = await Get<IAskBatch>(AskRules.BatchOf(Id, batch.Id)).All().ConfigureAwait(true);
            for (var index = batch.Next; index < batch.Count && index < asks.Count; index++)
            {
                keys.Add(asks[index].Key);
            }

            for (var j = 0; j < batch.Flying.Count; j++)
            {
                var index = batch.Flying[j].Index;
                if ((uint)index < (uint)asks.Count)
                {
                    keys.Add(asks[index].Key);
                }
            }

            for (var j = 0; j < batch.Waiting.Count; j++)
            {
                var index = batch.Waiting[j].Index;
                if ((uint)index < (uint)asks.Count)
                {
                    keys.Add(asks[index].Key);
                }
            }
        }

        return keys;
    }

    private static int FindBatchIndex(IReadOnlyList<OpenBatch> batches, string batchId)
    {
        for (var i = 0; i < batches.Count; i++)
        {
            if (string.Equals(batches[i].Id, batchId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindFlyingIndex(IReadOnlyList<FlyingAsk> flying, int index, int attempt)
    {
        for (var i = 0; i < flying.Count; i++)
        {
            if (flying[i].Index == index && flying[i].Attempt == attempt)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<T> RemoveAt<T>(IReadOnlyList<T> items, int removeIndex)
    {
        var next = new List<T>(items.Count - 1);
        for (var i = 0; i < items.Count; i++)
        {
            if (i != removeIndex)
            {
                next.Add(items[i]);
            }
        }

        return next;
    }

    private static bool HasDueFlying(IReadOnlyList<FlyingAsk> flying, DateTimeOffset now)
    {
        for (var i = 0; i < flying.Count; i++)
        {
            if (flying[i].Due <= now)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasFlyingOrWaiting(IReadOnlyList<OpenBatch> batches)
    {
        for (var i = 0; i < batches.Count; i++)
        {
            if (batches[i].Flying.Count > 0 || batches[i].Waiting.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> UnsettledIndexes(OpenBatch batch)
    {
        var indexes = new List<int>(batch.Flying.Count + batch.Waiting.Count + Math.Max(0, batch.Count - batch.Next));
        for (var i = 0; i < batch.Flying.Count; i++)
        {
            indexes.Add(batch.Flying[i].Index);
        }

        for (var i = 0; i < batch.Waiting.Count; i++)
        {
            indexes.Add(batch.Waiting[i].Index);
        }

        for (var index = batch.Next; index < batch.Count; index++)
        {
            indexes.Add(index);
        }

        indexes.Sort();
        for (var i = indexes.Count - 1; i > 0; i--)
        {
            if (indexes[i] == indexes[i - 1])
            {
                indexes.RemoveAt(i);
            }
        }

        return indexes;
    }

    private static int GroupSize(IReadOnlyList<Answer> unsent)
    {
        var max = Math.Min(unsent.Count, AskLimits.AnswersPerSignal);
        var bytes = ResultBytes(unsent[0]);
        var count = 1;
        while (count < max)
        {
            var nextBytes = ResultBytes(unsent[count]);
            if (bytes + nextBytes > AskLimits.MaxResultBytes)
            {
                break;
            }

            bytes += nextBytes;
            count++;
        }

        return count;
    }

    private static int ResultBytes(Answer answer) =>
        answer.Result.ValueKind == JsonValueKind.Undefined
            ? 0
            : Encoding.UTF8.GetByteCount(answer.Result.GetRawText());

    private static int OpenCount(IReadOnlyList<OpenBatch> batches)
    {
        var total = 0;
        for (var i = 0; i < batches.Count; i++)
        {
            total += batches[i].Count - batches[i].Settled;
        }

        return total;
    }

    private static int ReadyWaitingIndex(IReadOnlyList<WaitingAsk> waiting, DateTimeOffset now)
    {
        for (var i = 0; i < waiting.Count; i++)
        {
            if (waiting[i].NotBefore <= now)
            {
                return i;
            }
        }

        return -1;
    }

    private static TimeSpan LongestTimeout(List<Ask> asks)
    {
        var longest = TimeSpan.Zero;
        for (var i = 0; i < asks.Count; i++)
        {
            var requested = asks[i].Timeout ?? AskLimits.DefaultTimeout;
            var clamped = requested <= TimeSpan.Zero
                ? AskLimits.DefaultTimeout
                : requested < AskLimits.MaxTimeout
                    ? requested
                    : AskLimits.MaxTimeout;
            if (clamped > longest)
            {
                longest = clamped;
            }
        }

        return longest;
    }

    private static DateTimeOffset Earlier(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is { } existing && existing <= candidate ? existing : candidate;
}

internal sealed record AsksState(
    IReadOnlyList<OpenBatch> Batches,
    IReadOnlyList<Answer> Unsent,
    DateTimeOffset? UnsentSince,
    long Hour,
    int AskedThisHour,
    bool Tripped)
{
    public AsksState() : this([], [], null, 0, 0, false) { }
}

/// <summary>One accepted batch with asks still unsettled.</summary>
/// <param name="Id">The batch id within the unit.</param>
/// <param name="Count">Total accepted asks in this batch.</param>
/// <param name="Next">The first index never started. Indexes below it are settled, flying or waiting.</param>
/// <param name="Flying">Attempts currently in flight.</param>
/// <param name="Waiting">Repeatable asks waiting for retry backoff.</param>
/// <param name="Settled">Count of asks in this batch that have settled.</param>
/// <param name="Timeout">The longest clamped timeout of the batch's asks.</param>
/// <param name="AskedAt">When the batch was accepted.</param>
internal sealed record OpenBatch(
    string Id,
    int Count,
    int Next,
    IReadOnlyList<FlyingAsk> Flying,
    IReadOnlyList<WaitingAsk> Waiting,
    int Settled,
    TimeSpan Timeout,
    DateTimeOffset AskedAt);

internal sealed record FlyingAsk(int Index, int Attempt, DateTimeOffset Due);

internal sealed record WaitingAsk(int Index, int Attempt, DateTimeOffset NotBefore);
