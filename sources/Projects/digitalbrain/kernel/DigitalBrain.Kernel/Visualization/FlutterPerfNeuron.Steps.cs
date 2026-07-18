using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Visualization;
using DigitalBrain.Kernel.Visualization;

namespace DigitalBrain.Kernel.Tests.Visualization;

public sealed class FlutterPerfNeuronTests
{
    static readonly DateTimeOffset T0 = new(2026, 5, 21, 14, 0, 0, TimeSpan.Zero);
    static readonly FlutterPerfOptions Opts = new();

    [Fact]
    public async Task Tick_with_no_samples_broadcasts_no_card_and_no_hint()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Tick();

        cards.Broadcasts.Should().BeEmpty();
        hints.Emitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Smooth_samples_broadcast_card_no_hint()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Observe(MakeSample("web-1", "web", p50: 9, p95: 12, jank: 0.01, at: T0));
        await n.Tick();

        cards.Broadcasts.Should().ContainSingle()
            .Which.RootWidget.Should().Be("FlutterPerfCard");
        hints.Emitted.Should().BeEmpty("smooth on smooth is not a crossing");
    }

    [Fact]
    public async Task Sustained_red_crossing_emits_exactly_one_hint()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Observe(MakeSample("windows-1", "windows", p50: 20, p95: 40, jank: 0.2, at: T0));
        await n.Tick();
        clock.UtcNow = T0.AddMilliseconds(1500);
        await n.Observe(MakeSample("windows-1", "windows", p50: 22, p95: 42, jank: 0.21, at: clock.UtcNow));
        await n.Tick();

        var redHints = hints.Emitted.Where(h => h.Tier == "red").ToArray();
        redHints.Should().ContainSingle();
        redHints[0].ClientId.Should().Be("windows-1");

        clock.UtcNow = T0.AddMilliseconds(2500);
        await n.Observe(MakeSample("windows-1", "windows", p50: 22, p95: 42, jank: 0.21, at: clock.UtcNow));
        await n.Tick();
        hints.Emitted.Where(h => h.Tier == "red").Should().ContainSingle();
    }

    [Fact]
    public async Task Transient_crossing_does_not_emit_hint()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Observe(MakeSample("web-2", "web", p50: 22, p95: 40, jank: 0.2, at: T0));
        await n.Tick();
        clock.UtcNow = T0.AddMilliseconds(500);
        await n.Observe(MakeSample("web-2", "web", p50: 9, p95: 12, jank: 0.01, at: clock.UtcNow));
        await n.Tick();

        hints.Emitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Per_client_isolation()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Observe(MakeSample("web-3", "web", p50: 9, p95: 12, jank: 0.01, at: T0));
        await n.Observe(MakeSample("windows-3", "windows", p50: 22, p95: 42, jank: 0.2, at: T0));
        await n.Tick();

        clock.UtcNow = T0.AddMilliseconds(1500);
        await n.Observe(MakeSample("web-3", "web", p50: 9, p95: 12, jank: 0.01, at: clock.UtcNow));
        await n.Observe(MakeSample("windows-3", "windows", p50: 22, p95: 42, jank: 0.2, at: clock.UtcNow));
        await n.Tick();

        hints.Emitted.Should().ContainSingle()
            .Which.ClientId.Should().Be("windows-3");
        var lastCardJson = cards.Broadcasts.Last().DataJson;
        lastCardJson.Should().Contain("\"Tier\":\"red\"");
    }

    [Fact]
    public async Task Idle_client_is_dropped_from_next_broadcast()
    {
        var clock = new ManualClock(T0);
        var cards = new CapturingCardBroadcaster();
        var hints = new CapturingHintBroadcaster();
        var n = new TestableFlutterPerfNeuron(clock, cards, hints);

        await n.Observe(MakeSample("web-4", "web", p50: 9, p95: 12, jank: 0.01, at: T0));
        await n.Tick();
        clock.UtcNow = T0.AddSeconds(10);
        await n.Observe(MakeSample("web-5", "web", p50: 9, p95: 12, jank: 0.01, at: clock.UtcNow));
        await n.Tick();

        var lastCardJson = cards.Broadcasts.Last().DataJson;
        lastCardJson.Should().NotContain("web-4");
        lastCardJson.Should().Contain("web-5");
    }

    static FlutterPerfSample MakeSample(string clientId, string platform, double p50, double p95, double jank, DateTimeOffset at) =>
        new(ClientId:           clientId,
        SampleWindowId:     Guid.NewGuid().ToString(),
        FrameCount:         60,
        P50FrameMs:         p50,
        P95FrameMs:         p95,
        JankPct:            jank,
        WidgetCount:        400,
        GlowPainterCount:   4,
        RebuildsPerSecond:  380,
        Platform:           platform) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: Guid.NewGuid(),
            callerNeuronType: "client/flutter",
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: FlutterPerfNeuron.FlutterPerfNeuronType,
            timestamp: at
        ) };

    sealed class ManualClock(DateTimeOffset start)
    {
        public DateTimeOffset UtcNow { get; set; } = start;
    }

    sealed class CapturingCardBroadcaster : IFlutterPerfBroadcaster
    {
        public List<RfwCard> Broadcasts { get; } = [];
        public Task BroadcastAsync(RfwCard card, CancellationToken ct = default)
        {
            Broadcasts.Add(card);
            return Task.CompletedTask;
        }
    }

    sealed class CapturingHintBroadcaster : IFlutterPerfHintBroadcaster
    {
        public List<VisualLoadHint> Emitted { get; } = [];
        public Task BroadcastAsync(VisualLoadHint hint, CancellationToken ct = default)
        {
            Emitted.Add(hint);
            return Task.CompletedTask;
        }
        public async IAsyncEnumerable<VisualLoadHint> SubscribeAsync(string clientId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    sealed class TestableFlutterPerfNeuron
    {
        readonly ManualClock _clock;
        readonly CapturingCardBroadcaster _cards;
        readonly CapturingHintBroadcaster _hints;
        readonly FlutterPerfOptions _opts = Opts;
        readonly Dictionary<string, ClientWindow> _windows = new();
        string? _lastSignature;

        public TestableFlutterPerfNeuron(ManualClock clock,
            CapturingCardBroadcaster cards, CapturingHintBroadcaster hints)
        {
            _clock = clock; _cards = cards; _hints = hints;
        }

        public Task Observe(FlutterPerfSample s)
        {
            if (!_windows.TryGetValue(s.ClientId, out var w))
            {
                w = new ClientWindow(s.ClientId, s.Platform);
                _windows[s.ClientId] = w;
            }
            w.Append(new SampleSnapshot(s.FrameCount, s.P50FrameMs, s.P95FrameMs, s.JankPct), s.Timestamp);
            w.LastSampleAt = s.Timestamp;
            w.Platform = s.Platform;
            w.LastWidgetCount = s.WidgetCount;
            w.LastGlowPainterCount = s.GlowPainterCount;
            w.LastRebuildsPerSecond = s.RebuildsPerSecond;
            return Task.CompletedTask;
        }

        public async Task Tick()
        {
            var now = _clock.UtcNow;

            foreach (var (k, w) in _windows.ToArray())
                if ((now - w.LastSampleAt) > _opts.IdleTimeout) _windows.Remove(k);
            if (_windows.Count == 0) return;

            var rows = new List<ClientPerfRow>();
            foreach (var w in _windows.Values)
            {
                w.TrimWindow(now, _opts.WindowSeconds);
                var (p50, p95, jank) = FlutterPerfProjection.Aggregate(w.Samples);
                var tier = FlutterPerfProjection.ResolveTier(p95, _opts);

                if (tier != w.CurrentTier)
                {
                    if (w.CandidateTier != tier)
                    {
                        w.CandidateTier = tier;
                        w.CandidateSince = now;
                    }
                    else if ((now - w.CandidateSince) >= _opts.TierCrossingDebounce)
                    {
                        w.CurrentTier = tier;
                        await _hints.BroadcastAsync(new VisualLoadHint(ClientId:           w.ClientId,
        Tier:               tier,
        Reason:             $"p95 {p95:F1}ms over {(now - w.CandidateSince).TotalSeconds:F1}s") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: Guid.Empty,
            callerNeuronType: FlutterPerfNeuron.FlutterPerfNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "client/flutter",
            timestamp: now
        ) });
                    }
                }
                else
                {
                    w.CandidateTier = tier;
                    w.CandidateSince = now;
                }

                rows.Add(new ClientPerfRow(
                    w.ClientId, w.Platform, w.CurrentTier,
                    p50, p95, jank,
                    w.LastWidgetCount, w.LastGlowPainterCount, w.LastRebuildsPerSecond));
            }

            var worst = rows.Select(r => r.Tier).Aggregate("smooth", (acc, t) =>
                t == "red" ? "red" : (t == "strained" && acc != "red") ? "strained" : acc);
            var aggP95 = rows.Count == 0 ? 0 : rows.Max(r => r.P95FrameMs);
            var aggJank = rows.Count == 0 ? 0 : rows.Average(r => r.JankPct);
            var payload = new FlutterPerfCardPayload(
                Summary: new FlutterPerfCardSummary(worst, rows.Count, aggP95, aggJank),
                Clients: rows);

            var signature = FlutterPerfProjection.Signature(payload);
            if (signature == _lastSignature) return;
            _lastSignature = signature;

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            await _cards.BroadcastAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "FlutterPerfCard",
        DataJson:           json) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: Guid.NewGuid(),
            callerNeuronType: "FlutterPerfNeuron",
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: now
        ) });
        }

        sealed class ClientWindow(string clientId, string platform)
        {
            public string ClientId { get; } = clientId;
            public string Platform { get; set; } = platform;
            public DateTimeOffset LastSampleAt { get; set; }
            public string CurrentTier { get; set; } = "smooth";
            public string CandidateTier { get; set; } = "smooth";
            public DateTimeOffset CandidateSince { get; set; } = DateTimeOffset.MinValue;
            public List<TimedSample> TimedSamples { get; } = [];
            public IReadOnlyList<SampleSnapshot> Samples => TimedSamples.ConvertAll(ts => ts.Snapshot);
            public int LastWidgetCount { get; set; }
            public int LastGlowPainterCount { get; set; }
            public int LastRebuildsPerSecond { get; set; }

            public void Append(SampleSnapshot s, DateTimeOffset at)
                => TimedSamples.Add(new TimedSample(s, at));

            public void TrimWindow(DateTimeOffset now, TimeSpan window)
            {
                var cutoff = now - window;
                while (TimedSamples.Count > 0 && TimedSamples[0].At < cutoff)
                    TimedSamples.RemoveAt(0);
            }
        }

        record struct TimedSample(SampleSnapshot Snapshot, DateTimeOffset At);
    }
}
