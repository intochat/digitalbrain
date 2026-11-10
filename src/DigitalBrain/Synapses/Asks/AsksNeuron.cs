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

    protected override async Task OnActivate(CancellationToken cancellationToken) =>
        _state = await Context.State.Load<AsksState>().ConfigureAwait(true) ?? new AsksState();

    [On]
    public async Task OnAsked(Asked asked, CancellationToken ct)
    {
        _ = ct;
        if (asked is null
            || !string.Equals(asked.Unit, Id.Key, StringComparison.Ordinal)
            || string.IsNullOrEmpty(asked.Batch)
            || asked.Asks is not { Count: > 0 }
            || HasBatch(_state.Batches, asked.Batch))
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

    public Task Detach(CancellationToken ct) => Task.CompletedTask;

    public Task Settled(AskSettlement settlement) => Task.CompletedTask;

    public Task Wake() => Task.CompletedTask;

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
                    var nextWaiting = new List<WaitingAsk>(batch.Waiting.Count - 1);
                    for (var j = 0; j < batch.Waiting.Count; j++)
                    {
                        if (j != waitingIndex)
                        {
                            nextWaiting.Add(batch.Waiting[j]);
                        }
                    }

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

        ScheduleDue(now);
    }

    private async Task Flush(bool now = false)
    {
        _ = now;
        var flushed = false;
        while (_state.Unsent.Count > 0)
        {
            var count = Math.Min(_state.Unsent.Count, AskLimits.AnswersPerSignal);
            IReadOnlyList<Answer> group = [.. _state.Unsent.Take(count)];
            IReadOnlyList<Answer> remaining = count == _state.Unsent.Count ? [] : [.. _state.Unsent.Skip(count)];
            var next = _state with
            {
                Unsent = remaining,
                UnsentSince = remaining.Count == 0 ? null : _state.UnsentSince,
            };
            await Save(next, [new Answered(Id.Key, group)]).ConfigureAwait(true);
            flushed = true;
        }

        if (flushed)
        {
            ScheduleDue(Context.Time.GetUtcNow());
        }
    }

    private async Task Save(AsksState next, IReadOnlyList<Signal> signals)
    {
        await Context.State.Save(next, signals).ConfigureAwait(true);
        _state = next;
    }

    private void ScheduleDue(DateTimeOffset now)
    {
        DateTimeOffset? earliest = null;
        for (var i = 0; i < _state.Batches.Count; i++)
        {
            var batch = _state.Batches[i];
            for (var j = 0; j < batch.Flying.Count; j++)
            {
                earliest = Earlier(earliest, batch.Flying[j].Due);
            }

            for (var j = 0; j < batch.Waiting.Count; j++)
            {
                earliest = Earlier(earliest, batch.Waiting[j].NotBefore);
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

    private static bool HasBatch(IReadOnlyList<OpenBatch> batches, string batchId)
    {
        for (var i = 0; i < batches.Count; i++)
        {
            if (string.Equals(batches[i].Id, batchId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
