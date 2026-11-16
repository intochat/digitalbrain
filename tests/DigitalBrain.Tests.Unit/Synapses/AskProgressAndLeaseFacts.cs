using System.Reflection;
using System.Text.Json;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class AskProgressAndLeaseFacts
{
    private static readonly BrainId Brain = new("alice");
    private static readonly NeuronId AsksId = new(Brain, AskRules.AsksNeuronType, "asker");
    private static readonly NeuronId BatchId = AskRules.BatchOf(AsksId, "b1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AskProgressedIsMarkedEphemeralWithUnitAskProgressedAlias()
    {
        var signal = typeof(AskProgressed).GetCustomAttribute<SignalAttribute>();
        Assert.NotNull(signal);
        Assert.Equal("unit.ask.progressed", signal.Alias);
        Assert.True(typeof(AskProgressed).IsDefined(typeof(EphemeralSignalAttribute), inherit: false));
        Assert.Equal(TimeSpan.FromMilliseconds(250), AskLimits.ProgressThrottle);
    }

    [Fact]
    public async Task AskRunReportDeliversEphemeralProgressAndCoalescesSub250MsTicksWithoutJournalWrites()
    {
        var clock = new ManualTimerClock();
        var hub = new AskProgressHub(clock);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

        var asksTimers = new RecordingTimers();
        var asksContext = new RecordingContext(AsksId)
        {
            Time = clock,
            RecordedTimers = asksTimers,
            RecordedReminders = asksTimers,
        };

        var probe = new FakeProbe();
        var resolver = new FakeSlotResolver(new Dictionary<string, object> { ["feed"] = probe });
        var batchContext = new RecordingContext(BatchId)
        {
            Time = clock,
        };

        var asks = new AsksNeuron(catalog, progress: hub);
        NeuronAttachment.Attach(asks, asksContext);
        await NeuronAttachment.Activate(asks, Ct);

        var batch = new AskBatchNeuron(catalog, resolver, progress: hub);
        NeuronAttachment.Attach(batch, batchContext);
        await NeuronAttachment.Activate(batch, Ct);

        asksContext.Neurons[BatchId] = new DeferredBatchStub(batch);
        batchContext.Neurons[AsksId] = asks;

        var receivedBySubscriber = new List<AskProgressed>();
        var receivedByKeySubscriber = new List<AskProgressed>();
        using var allSub = hub.Subscribe(receivedBySubscriber.Add, unit: "asker");
        using var keySub = hub.Subscribe(receivedByKeySubscriber.Add, unit: "asker", runId: "sandbox-build");

        var writesDuringProgressTicks = 0;
        string? capturedRunId = null;
        probe.OnGet = async (_, ct) =>
        {
            Assert.NotNull(AskRun.Current);
            capturedRunId = AskRun.Current!.RunId;
            Assert.Equal("asker", AskRun.Current.Unit);
            Assert.Equal("sandbox-build", AskRun.Current.Key);

            var writesAtRunStart = asksContext.Writes.Count;

            // Rapid sub-250ms identical ticks are coalesced into one notification.
            await AskRun.Report("Compiling workspace", 0.25, ct);
            clock.Advance(TimeSpan.FromMilliseconds(50));
            await AskRun.Report("Compiling workspace", 0.25, ct);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await AskRun.Report("Compiling workspace", 0.25, ct);

            // Advancing past 250ms allows the next identical tick through.
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await AskRun.Report("Compiling workspace", 0.25, ct);

            // Distinct status or fraction reports immediately even within 250ms, including via IAskProgress.ReportProgress.
            clock.Advance(TimeSpan.FromMilliseconds(40));
            await hub.ReportProgress(capturedRunId, "Running tests", 0.75, ct);
            clock.Advance(TimeSpan.FromMilliseconds(40));
            await AskRun.Report("Done", 1.0, ct);

            writesDuringProgressTicks = asksContext.Writes.Count - writesAtRunStart;
            return JsonElement.Parse("""{"ok":true}""");
        };

        var ask = new Ask("feed", "Get", JsonElement.Parse("""{"id":"1"}"""), "sandbox-build");
        await asks.OnAsked(new Asked("asker", "b1", [ask]), Ct);

        await ((DeferredBatchStub)asksContext.Neurons[BatchId]).DrainRunsAsync();
        Assert.Equal(0, writesDuringProgressTicks);

        // Settled writes once (and Flush writes once for Answered), while all progress notifications went through PublishEphemeral only.
        Assert.Equal(4, receivedBySubscriber.Count);
        Assert.Equal(4, receivedByKeySubscriber.Count);
        Assert.Equal(
            [
                ("Compiling workspace", 0.25),
                ("Compiling workspace", 0.25),
                ("Running tests", 0.75),
                ("Done", 1.0),
            ],
            receivedBySubscriber.Select(p => (p.Status, p.Fraction!.Value)));

        // Every coalesced-through progress event was published ephemerally (never saved via Seat.Save).
        Assert.Equal(4, asksContext.Published.OfType<AskProgressed>().Count());
        Assert.DoesNotContain(asksContext.Writes, w => w.Signals.Any(s => s is AskProgressed));
        Assert.Null(AskRun.Current);
    }

    [Fact]
    public async Task LongRunningKeepAliveAskRenewsLeaseAcrossMultipleTimeoutWindowsWithoutPrematureTimeout()
    {
        var clock = new ManualTimerClock();
        var hub = new AskProgressHub(clock);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

        var asksTimers = new RecordingTimers();
        var asksContext = new RecordingContext(AsksId)
        {
            Time = clock,
            RecordedTimers = asksTimers,
            RecordedReminders = asksTimers,
        };

        var probe = new FakeProbe();
        var resolver = new FakeSlotResolver(new Dictionary<string, object> { ["feed"] = probe });
        var batchContext = new RecordingContext(BatchId)
        {
            Time = clock,
        };

        var asks = new AsksNeuron(catalog, progress: hub);
        NeuronAttachment.Attach(asks, asksContext);
        await NeuronAttachment.Activate(asks, Ct);

        var batch = new AskBatchNeuron(catalog, resolver, progress: hub);
        NeuronAttachment.Attach(batch, batchContext);
        await NeuronAttachment.Activate(batch, Ct);

        var stub = new DeferredBatchStub(batch);
        asksContext.Neurons[BatchId] = stub;
        batchContext.Neurons[AsksId] = asks;

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = default;
        probe.OnGet = async (_, ct) =>
        {
            observedToken = ct;
            return await completion.Task.WaitAsync(ct);
        };

        // Timeout is 20s, KeepAlive = true; we will advance the clock by 90s (4.5x timeout) in 10s steps!
        var ask = new Ask("feed", "Get", JsonElement.Parse("""{"id":"long"}"""), "coder-run")
        {
            Timeout = TimeSpan.FromSeconds(20),
            KeepAlive = true,
        };

        await asks.OnAsked(new Asked("asker", "b1", [ask]), Ct);
        var runTask = stub.StartNextRun();

        for (var step = 0; step < 9; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            await asks.Remind("asks-due", Ct);
            await asks.Remind("asks-nudge", Ct);
            Assert.False(observedToken.IsCancellationRequested);
            Assert.False(runTask.IsCompleted);
        }

        completion.SetResult(JsonElement.Parse("""{"completed":"after-90s"}"""));
        await runTask;
        await asks.Remind("asks-due", Ct);

        var answered = Assert.Single(asksContext.Writes.SelectMany(w => w.Signals.OfType<Answered>()));
        var answer = Assert.Single(answered.Answers);
        Assert.Equal(AnswerStatus.Ok, answer.Status);
        Assert.Equal("coder-run", answer.Key);
        Assert.Equal("""{"completed":"after-90s"}""", answer.Result.GetRawText());
    }

    [Fact]
    public async Task ExplicitHeartbeatExtendsBothBatchCancellationAndAsksSweepWhileUnrenewedAskTimesOut()
    {
        var clock = new ManualTimerClock();
        var hub = new AskProgressHub(clock);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

        var asksTimers = new RecordingTimers();
        var asksContext = new RecordingContext(AsksId)
        {
            Time = clock,
            RecordedTimers = asksTimers,
            RecordedReminders = asksTimers,
        };

        var probe = new FakeProbe();
        var resolver = new FakeSlotResolver(new Dictionary<string, object> { ["feed"] = probe });
        var batchContext = new RecordingContext(BatchId)
        {
            Time = clock,
        };

        var asks = new AsksNeuron(catalog, progress: hub);
        NeuronAttachment.Attach(asks, asksContext);
        await NeuronAttachment.Activate(asks, Ct);

        var batch = new AskBatchNeuron(catalog, resolver, progress: hub);
        NeuronAttachment.Attach(batch, batchContext);
        await NeuronAttachment.Activate(batch, Ct);

        var stub = new DeferredBatchStub(batch);
        asksContext.Neurons[BatchId] = stub;
        batchContext.Neurons[AsksId] = asks;

        var stepGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AskRun? ambient = null;
        probe.OnGet = async (id, ct) =>
        {
            if (id == "stalled")
            {
                await Task.Delay(Timeout.Infinite, ct);
                return default;
            }

            ambient = AskRun.Current;
            await stepGate.Task.WaitAsync(ct);
            return JsonElement.Parse("""{"ok":true}""");
        };

        var renewedAsk = new Ask("feed", "Get", JsonElement.Parse("""{"id":"renewed"}"""), "renewed")
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        var stalledAsk = new Ask("feed", "Get", JsonElement.Parse("""{"id":"stalled"}"""), "stalled")
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        await asks.OnAsked(new Asked("asker", "b1", [renewedAsk, stalledAsk]), Ct);
        var renewedTask = stub.StartNextRun();
        var stalledTask = stub.StartNextRun();

        Assert.NotNull(ambient);

        // Advance 7s, send explicit heartbeat only for renewedAsk, then advance another 7s (14s total > 10s timeout).
        clock.Advance(TimeSpan.FromSeconds(7));
        Assert.True(await ambient!.RenewLease(TimeSpan.FromSeconds(10), Ct));

        clock.Advance(TimeSpan.FromSeconds(7));
        await stalledTask; // Stalled ask timed out at 10s!
        await asks.Remind("asks-due", Ct);

        Assert.False(renewedTask.IsCompleted); // Renewed ask is still alive at 14s!

        stepGate.SetResult();
        await renewedTask;
        await asks.Remind("asks-due", Ct);

        var answers = asksContext.Writes
            .SelectMany(w => w.Signals.OfType<Answered>())
            .SelectMany(a => a.Answers)
            .ToDictionary(a => a.Key);

        Assert.Equal(AnswerStatus.Unknown, answers["stalled"].Status);
        Assert.Equal("timeout", answers["stalled"].Error);
        Assert.Equal(AnswerStatus.Ok, answers["renewed"].Status);
    }

    [Fact]
    public async Task ExplicitRenewPersistsExtendedDueAcrossActivationRestart()
    {
        var clock = new ManualTimerClock();
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), typeof(AskerFunctions)]));

        var timers = new RecordingTimers();
        var context = new RecordingContext(AsksId)
        {
            Time = clock,
            RecordedTimers = timers,
            RecordedReminders = timers,
        };

        var asks1 = new AsksNeuron(catalog);
        NeuronAttachment.Attach(asks1, context);
        await NeuronAttachment.Activate(asks1, Ct);

        var batchStub = new RecordingBatchStub(
            [new Ask("feed", "Get", JsonElement.Parse("""{"id":"1"}"""), "long-job") { Timeout = TimeSpan.FromSeconds(10) }]);
        context.Neurons[BatchId] = batchStub;

        await asks1.OnAsked(new Asked("asker", "b1", batchStub.Stored), Ct);
        Assert.Equal([(0, 1)], batchStub.Runs);

        // Renew at t0 + 7s with a 10s extension -> due must persist at t0 + 7s + 10s + Grace (22s).
        clock.Advance(TimeSpan.FromSeconds(7));
        context.Caller = DigitalBrain.Access.Caller.OfKernel(Brain, BatchId);
        await asks1.Renew(new AskLeaseRenewal("b1", 0, 1, TimeSpan.FromSeconds(10)));
        context.Caller = null;

        // Advance past original deadline (t0 + 15s) to t0 + 16s and simulate reactivation via fresh AsksNeuron on same Seat.
        clock.Advance(TimeSpan.FromSeconds(9));
        var asks2 = new AsksNeuron(catalog);
        NeuronAttachment.Attach(asks2, context);
        await NeuronAttachment.Activate(asks2, Ct);

        Assert.Empty(context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).SelectMany(a => a.Answers));

        // Advancing past renewed deadline (t0 + 22s) triggers timeout on Sweep.
        clock.Advance(TimeSpan.FromSeconds(6));
        await asks2.Remind("asks-due", Ct);

        var answer = Assert.Single(context.Writes.SelectMany(w => w.Signals.OfType<Answered>()).SelectMany(a => a.Answers));
        Assert.Equal(("long-job", AnswerStatus.Unknown, "timeout"), (answer.Key, answer.Status, answer.Error));
    }

    private sealed class RecordingBatchStub(IReadOnlyList<Ask> stored) : IAskBatch
    {
        public IReadOnlyList<Ask> Stored { get; private set; } = stored;
        public List<(int Index, int Attempt)> Runs { get; } = [];
        public bool Dropped { get; private set; }

        public Task Put(string unit, IReadOnlyList<Ask> asks)
        {
            Stored = [.. asks];
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Ask>> All() => Task.FromResult(Stored);

        public Task Run(int index, int attempt)
        {
            Runs.Add((index, attempt));
            return Task.CompletedTask;
        }

        public Task Drop()
        {
            Dropped = true;
            Stored = [];
            return Task.CompletedTask;
        }
    }

    private sealed class DeferredBatchStub(AskBatchNeuron inner) : IAskBatch
    {
        private readonly Queue<(int Index, int Attempt)> _queued = new();

        public Task Put(string unit, IReadOnlyList<Ask> asks) => inner.Put(unit, asks);

        public Task<IReadOnlyList<Ask>> All() => inner.All();

        public Task Run(int index, int attempt)
        {
            _queued.Enqueue((index, attempt));
            return Task.CompletedTask;
        }

        public Task StartNextRun()
        {
            var (index, attempt) = _queued.Dequeue();
            return inner.Run(index, attempt);
        }

        public async Task DrainRunsAsync()
        {
            while (_queued.Count > 0)
            {
                await StartNextRun();
            }
        }

        public Task Drop() => inner.Drop();
    }

    private sealed class RecordingTimers : ITimers, IReminders
    {
        public List<(string Name, TimeSpan Due, TimeSpan? Period, bool KeepAlive)> Timers { get; } = [];
        public List<(string Name, TimeSpan Period)> Reminders { get; } = [];

        public void Start(string name, TimeSpan due, TimeSpan? period) => Timers.Add((name, due, period, false));

        public void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive) => Timers.Add((name, due, period, keepAlive));

        public void Stop(string name) { }

        Task IReminders.Start(string name, TimeSpan due, TimeSpan period)
        {
            Reminders.Add((name, period));
            return Task.CompletedTask;
        }

        Task IReminders.Stop(string name) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> Names() =>
            Task.FromResult<IReadOnlyList<string>>([.. Reminders.Select(r => r.Name).Distinct(StringComparer.Ordinal)]);
    }

    private sealed class ManualTimerClock : TimeProvider
    {
        private readonly Lock _gate = new();
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate)
            {
                return _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var entry = new Entry(this, callback, state);
            entry.Change(dueTime, period);
            return entry;
        }

        public void Advance(TimeSpan delta)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
            DateTimeOffset target;
            lock (_gate)
            {
                target = _now + delta;
            }

            while (true)
            {
                Entry? next = null;
                DateTimeOffset nextDue = default;
                lock (_gate)
                {
                    foreach (var timer in _timers)
                    {
                        if (!timer.Disposed && timer.Due is { } due && due <= target && (next is null || due < nextDue))
                        {
                            next = timer;
                            nextDue = due;
                        }
                    }

                    if (next is null)
                    {
                        _now = target;
                        return;
                    }

                    _now = nextDue;
                    next.PrepareFire(nextDue);
                }

                next.Fire();
            }
        }

        private sealed class Entry : ITimer
        {
            private readonly ManualTimerClock _clock;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            public Entry(ManualTimerClock clock, TimerCallback callback, object? state)
            {
                _clock = clock;
                _callback = callback;
                _state = state;
                lock (_clock._gate)
                {
                    _clock._timers.Add(this);
                }
            }

            public DateTimeOffset? Due { get; private set; }
            public TimeSpan Period { get; private set; } = Timeout.InfiniteTimeSpan;
            public bool Disposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_clock._gate)
                {
                    if (Disposed)
                    {
                        return false;
                    }

                    Period = period;
                    Due = dueTime == Timeout.InfiniteTimeSpan
                        ? null
                        : _clock._now + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
                    return true;
                }
            }

            public void PrepareFire(DateTimeOffset firedAt)
            {
                Due = Period > TimeSpan.Zero && Period != Timeout.InfiniteTimeSpan
                    ? firedAt + Period
                    : null;
            }

            public void Fire() => _callback(_state);

            public void Dispose()
            {
                lock (_clock._gate)
                {
                    Disposed = true;
                    Due = null;
                    _clock._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
