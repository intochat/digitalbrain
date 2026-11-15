using System.Text;
using System.Text.Json;

namespace DigitalBrain.Units;

internal sealed record AsksState(
    IReadOnlyList<OpenBatch> Batches,
    IReadOnlyList<Answer> Unsent,
    DateTimeOffset? UnsentSince,
    long Hour,
    int AskedThisHour,
    bool Tripped)
{
    public AsksState() : this([], [], null, 0, 0, false) { }

    internal bool ContainsBatch(string batchId) => FindBatchIndex(Batches, batchId) >= 0;

    internal bool IsTrippedAt(DateTimeOffset now)
    {
        var hour = now.ToUnixTimeSeconds() / 3600;
        return hour == Hour && Tripped;
    }

    internal bool HasBudgetFor(int count, DateTimeOffset now)
    {
        var hour = now.ToUnixTimeSeconds() / 3600;
        var askedThisHour = hour == Hour ? AskedThisHour : 0;
        var tripped = hour == Hour && Tripped;
        return !tripped
            && OpenCount() + count <= AskLimits.MaxOpenAsks
            && askedThisHour + count <= AskLimits.MaxAsksPerHour;
    }

    internal int OpenCount()
    {
        var total = 0;
        for (var i = 0; i < Batches.Count; i++)
        {
            total += Batches[i].Count - Batches[i].Settled;
        }

        return total;
    }

    internal bool HasFlyingOrWaiting()
    {
        for (var i = 0; i < Batches.Count; i++)
        {
            if (Batches[i].Flying.Count > 0 || Batches[i].Waiting.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal AcceptOutcome Accept(string batchId, IReadOnlyList<Ask> asks, IReadOnlySet<string> openKeys, DateTimeOffset now)
    {
        var hour = now.ToUnixTimeSeconds() / 3600;
        var askedThisHour = AskedThisHour;
        var tripped = Tripped;
        if (hour != Hour)
        {
            askedThisHour = 0;
            tripped = false;
        }

        if (tripped || string.IsNullOrEmpty(batchId) || asks.Count == 0 || FindBatchIndex(Batches, batchId) >= 0)
        {
            return new AcceptOutcome(this, Save: false, Accepted: [], CreatedAnswers: false);
        }

        var n = asks.Count;
        var open = OpenCount();
        var batches = Batches;
        var unsent = new List<Answer>(Unsent);
        var unsentSince = UnsentSince;
        var createdAnswers = false;

        if (open + n > AskLimits.MaxOpenAsks || askedThisHour + n > AskLimits.MaxAsksPerHour)
        {
            unsentSince ??= now;
            for (var i = 0; i < n; i++)
            {
                var ask = asks[i];
                unsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, AnswerStatus.Refused, AskRules.NoResult, "over-budget", open));
            }

            tripped = true;
            createdAnswers = true;
            return new AcceptOutcome(new AsksState(batches, unsent, unsentSince, hour, askedThisHour, tripped), Save: true, Accepted: [], CreatedAnswers: createdAnswers);
        }

        var seenKeys = new HashSet<string>(openKeys, StringComparer.Ordinal);
        var accepted = new List<Ask>(n);
        var refused = new List<Ask>();
        for (var i = 0; i < n; i++)
        {
            var ask = asks[i];
            if (seenKeys.Add(ask.Key))
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
            unsentSince ??= now;
            for (var i = 0; i < refused.Count; i++)
            {
                var ask = refused[i];
                unsent.Add(new Answer(ask.Slot, ask.Operation, ask.Key, AnswerStatus.Refused, AskRules.NoResult, "already-open", openAfter));
            }

            createdAnswers = true;
        }

        if (accepted.Count > 0)
        {
            batches = [.. batches, new OpenBatch(batchId, accepted.Count, 0, [], [], 0, LongestTimeout(accepted), now)];
            askedThisHour += accepted.Count;
        }

        return new AcceptOutcome(new AsksState(batches, unsent, unsentSince, hour, askedThisHour, tripped), Save: true, Accepted: accepted, CreatedAnswers: createdAnswers);
    }

    internal StartOutcome Start(DateTimeOffset now, TimeSpan grace)
    {
        var batches = Batches.ToArray();
        var flyingCount = 0;
        for (var i = 0; i < batches.Length; i++)
        {
            flyingCount += batches[i].Flying.Count;
        }

        List<StartedAsk>? started = null;
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
                    IReadOnlyList<FlyingAsk> nextFlying = [.. batch.Flying, new FlyingAsk(waiting.Index, waiting.Attempt, now + batch.Timeout + grace)];
                    batches[i] = batch with { Flying = nextFlying, Waiting = nextWaiting };
                    (started ??= []).Add(new StartedAsk(batch.Id, waiting.Index, waiting.Attempt));
                    flyingCount++;
                    picked = true;
                    break;
                }

                if (batch.Next < batch.Count)
                {
                    var index = batch.Next;
                    IReadOnlyList<FlyingAsk> nextFlying = [.. batch.Flying, new FlyingAsk(index, 1, now + batch.Timeout + grace)];
                    batches[i] = batch with { Next = index + 1, Flying = nextFlying };
                    (started ??= []).Add(new StartedAsk(batch.Id, index, 1));
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

        if (started is not { Count: > 0 })
        {
            return new StartOutcome(this, []);
        }

        return new StartOutcome(this with { Batches = batches }, started);
    }

    internal SettleOutcome Settle(AskSettlement? settlement, DateTimeOffset now)
    {
        if (settlement is null || string.IsNullOrEmpty(settlement.Batch))
        {
            return new SettleOutcome(this, Changed: false, DroppedBatchId: null);
        }

        var batchIndex = FindBatchIndex(Batches, settlement.Batch);
        if (batchIndex < 0)
        {
            return new SettleOutcome(this, Changed: false, DroppedBatchId: null);
        }

        var batch = Batches[batchIndex];
        var flyingIndex = FindFlyingIndex(batch.Flying, settlement.Index, settlement.Attempt);
        if (flyingIndex < 0)
        {
            return new SettleOutcome(this, Changed: false, DroppedBatchId: null);
        }

        var nextFlying = RemoveAt(batch.Flying, flyingIndex);
        var nextBatches = new List<OpenBatch>(Batches.Count);

        if (settlement.Transient && settlement.Repeatable && settlement.Attempt < AskLimits.MaxAttempts)
        {
            var delay = RetryPolicy.Next(settlement.Attempt);
            IReadOnlyList<WaitingAsk> nextWaiting = [.. batch.Waiting, new WaitingAsk(settlement.Index, settlement.Attempt + 1, now + delay)];
            for (var i = 0; i < Batches.Count; i++)
            {
                nextBatches.Add(i == batchIndex ? batch with { Flying = nextFlying, Waiting = nextWaiting } : Batches[i]);
            }

            return new SettleOutcome(this with { Batches = nextBatches }, Changed: true, DroppedBatchId: null);
        }

        var nextSettled = batch.Settled + 1;
        var remaining = OpenCount() - 1;
        var answer = new Answer(settlement.Slot, settlement.Operation, settlement.Key, settlement.Status, settlement.Result, settlement.Error, remaining, settlement.Detail);
        IReadOnlyList<Answer> nextUnsent = [.. Unsent, answer];
        var nextUnsentSince = Unsent.Count == 0 ? now : UnsentSince ?? now;
        var closed = nextSettled == batch.Count && nextFlying.Count == 0 && batch.Waiting.Count == 0;

        for (var i = 0; i < Batches.Count; i++)
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
                nextBatches.Add(Batches[i]);
            }
        }

        return new SettleOutcome(
            this with { Batches = nextBatches, Unsent = nextUnsent, UnsentSince = nextUnsentSince },
            Changed: true,
            DroppedBatchId: closed ? batch.Id : null);
    }

    internal IReadOnlyList<string> BatchesNeedingAsks(DateTimeOffset now)
    {
        if (Batches.Count == 0)
        {
            return [];
        }

        List<string>? ids = null;
        for (var i = 0; i < Batches.Count; i++)
        {
            var batch = Batches[i];
            if (batch.AskedAt + AskLimits.MaxAge <= now || HasDueFlying(batch.Flying, now))
            {
                (ids ??= []).Add(batch.Id);
            }
        }

        return ids ?? (IReadOnlyList<string>)[];
    }

    internal SweepOutcome Sweep(IReadOnlyDictionary<string, IReadOnlyList<Ask>> storedAsksByBatch, DateTimeOffset now)
    {
        if (Batches.Count == 0)
        {
            return new SweepOutcome(this, Changed: false, DroppedBatchIds: []);
        }

        var openRemaining = OpenCount();
        var nextBatches = new List<OpenBatch>(Batches.Count);
        var nextUnsent = new List<Answer>(Unsent);
        var nextUnsentSince = UnsentSince;
        List<string>? droppedBatchIds = null;
        var changed = false;

        for (var i = 0; i < Batches.Count; i++)
        {
            var batch = Batches[i];
            if (batch.AskedAt + AskLimits.MaxAge <= now)
            {
                changed = true;
                var asks = storedAsksByBatch.GetValueOrDefault(batch.Id, []);
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
            var storedAsks = storedAsksByBatch.GetValueOrDefault(batch.Id, []);
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
                        var delay = RetryPolicy.Next(flying.Attempt);
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

        if (!changed)
        {
            return new SweepOutcome(this, Changed: false, DroppedBatchIds: []);
        }

        return new SweepOutcome(
            this with { Batches = nextBatches, Unsent = nextUnsent, UnsentSince = nextUnsentSince },
            Changed: true,
            DroppedBatchIds: droppedBatchIds ?? (IReadOnlyList<string>)[]);
    }

    internal LeaseOutcome ExtendLease(string batchId, int index, int attempt, TimeSpan? extension, DateTimeOffset now, TimeSpan grace)
    {
        var batchIndex = FindBatchIndex(Batches, batchId);
        if (batchIndex < 0)
        {
            return new LeaseOutcome(this, Matched: false, Changed: false, TimeSpan.Zero, TimeSpan.Zero);
        }

        var batch = Batches[batchIndex];
        var flyingIndex = FindFlyingIndex(batch.Flying, index, attempt);
        if (flyingIndex < 0)
        {
            return new LeaseOutcome(this, Matched: false, Changed: false, TimeSpan.Zero, TimeSpan.Zero);
        }

        var maxExpiry = batch.AskedAt + AskLimits.MaxAge;
        if (now >= maxExpiry)
        {
            return new LeaseOutcome(this, Matched: false, Changed: false, TimeSpan.Zero, TimeSpan.Zero);
        }

        var clamped = ClampTimeout(extension ?? batch.Timeout, batch.Timeout);
        var candidate = now + clamped + grace;
        if (candidate > maxExpiry)
        {
            candidate = maxExpiry;
        }

        var flying = batch.Flying[flyingIndex];
        var previousRemaining = flying.Due - now;
        if (candidate <= flying.Due)
        {
            return new LeaseOutcome(this, Matched: true, Changed: false, previousRemaining, clamped);
        }

        var nextFlying = new List<FlyingAsk>(batch.Flying.Count);
        for (var i = 0; i < batch.Flying.Count; i++)
        {
            nextFlying.Add(i == flyingIndex ? flying with { Due = candidate } : batch.Flying[i]);
        }

        var nextBatches = new List<OpenBatch>(Batches.Count);
        for (var i = 0; i < Batches.Count; i++)
        {
            nextBatches.Add(i == batchIndex ? batch with { Flying = nextFlying } : Batches[i]);
        }

        return new LeaseOutcome(this with { Batches = nextBatches }, Matched: true, Changed: true, previousRemaining, clamped);
    }

    internal bool ShouldFlush(bool now, DateTimeOffset clockNow) =>
        now
        || Unsent.Count >= AskLimits.AnswersPerSignal
        || !HasFlyingOrWaiting()
        || UnsentSince is not { } since
        || clockNow >= since + AskLimits.AnswerFlush;

    internal FlushOutcome? NextFlushGroup()
    {
        if (Unsent.Count == 0)
        {
            return null;
        }

        var count = GroupSize(Unsent);
        IReadOnlyList<Answer> group = [.. Unsent.Take(count)];
        IReadOnlyList<Answer> remaining = count == Unsent.Count ? [] : [.. Unsent.Skip(count)];
        var next = this with
        {
            Unsent = remaining,
            UnsentSince = remaining.Count == 0 ? null : UnsentSince,
        };
        return new FlushOutcome(next, group);
    }

    internal DateTimeOffset? NextDue()
    {
        var flyingCount = 0;
        for (var i = 0; i < Batches.Count; i++)
        {
            flyingCount += Batches[i].Flying.Count;
        }

        DateTimeOffset? earliest = null;
        for (var i = 0; i < Batches.Count; i++)
        {
            var batch = Batches[i];
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

        if (Unsent.Count > 0 && UnsentSince is { } unsentSince)
        {
            earliest = Earlier(earliest, unsentSince + AskLimits.AnswerFlush);
        }

        return earliest;
    }

    internal static List<int> UnsettledIndexes(OpenBatch batch)
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
            var clamped = ClampTimeout(asks[i].Timeout ?? AskLimits.DefaultTimeout, AskLimits.DefaultTimeout);
            if (clamped > longest)
            {
                longest = clamped;
            }
        }

        return longest;
    }

    private static TimeSpan ClampTimeout(TimeSpan requested, TimeSpan fallback) =>
        requested <= TimeSpan.Zero
            ? fallback
            : requested < AskLimits.MaxTimeout
                ? requested
                : AskLimits.MaxTimeout;

    private static DateTimeOffset Earlier(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is { } existing && existing <= candidate ? existing : candidate;
}

internal sealed record AcceptOutcome(AsksState State, bool Save, IReadOnlyList<Ask> Accepted, bool CreatedAnswers);

internal sealed record StartedAsk(string BatchId, int Index, int Attempt);

internal sealed record StartOutcome(AsksState State, IReadOnlyList<StartedAsk> Started);

internal sealed record SettleOutcome(AsksState State, bool Changed, string? DroppedBatchId);

internal sealed record SweepOutcome(AsksState State, bool Changed, IReadOnlyList<string> DroppedBatchIds);

internal sealed record LeaseOutcome(AsksState State, bool Matched, bool Changed, TimeSpan PreviousRemaining, TimeSpan EffectiveWindow);

internal sealed record FlushOutcome(AsksState State, IReadOnlyList<Answer> Group);

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
