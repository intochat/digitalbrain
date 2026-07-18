using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Visualization;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Visualization;

// Kernel-resident projection of Flutter rendering health into a FlutterPerfCard
// and a per-clientId VisualLoadHint stream. Single input source — no observer
// grain needed; samples arrive via HandleSynapseAsync on the implicit stream.
[ImplicitStreamSubscription(FlutterPerfNeuronType)]
public sealed class FlutterPerfNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IFlutterPerfBroadcaster cards,
    IFlutterPerfHintBroadcaster hints,
    IOptions<FlutterPerfOptions> options,
    TimeProvider time,
    ILogger<FlutterPerfNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IFlutterPerfNeuron, INeuronMetadata,
      IHandle<FlutterPerfSample>
{
    public const string FlutterPerfNeuronType = nameof(FlutterPerfNeuron);

    public static NeuronId Id => new("kernel/flutter-perf");
    public static string Icon => "flutter";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    readonly Dictionary<string, ClientWindow> _windows = new();
    string? _lastSignature;

    protected override Task HandleSynapseAsync(Synapse synapse) => synapse switch
    {
        FlutterPerfSample sample => OnSample(sample),
        _ => Task.CompletedTask,
    };

    Task OnSample(FlutterPerfSample s)
    {
        Counter("flutterperf.samples_ingested").Increment(1);
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
        Histogram("flutterperf.window_size").Record(w.TimedSamples.Count);
        return Task.CompletedTask;
    }

    public async Task Tick()
    {
        var now = time.GetUtcNow();
        var opts = options.Value;

        foreach (var (k, w) in _windows.ToArray())
            if ((now - w.LastSampleAt) > opts.IdleTimeout) _windows.Remove(k);
        if (_windows.Count == 0) return;

        var rows = new List<ClientPerfRow>();
        foreach (var w in _windows.Values)
        {
            w.TrimWindow(now, opts.WindowSeconds);
            var (p50, p95, jank) = FlutterPerfProjection.Aggregate(w.Samples);
            Histogram("flutterperf.p95_ms").Record(p95);
            var tier = FlutterPerfProjection.ResolveTier(p95, opts);

            if (tier != w.CurrentTier)
            {
                if (w.CandidateTier != tier)
                {
                    w.CandidateTier = tier;
                    w.CandidateSince = now;
                }
                else if ((now - w.CandidateSince) >= opts.TierCrossingDebounce)
                {
                    w.CurrentTier = tier;
                    await hints.BroadcastAsync(new VisualLoadHint(ClientId:           w.ClientId,
        Tier:               tier,
        Reason:             $"p95 {p95:F1}ms over {(now - w.CandidateSince).TotalSeconds:F1}s") { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.NewGuid(),
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: FlutterPerfNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "client/flutter",
            timestamp: now
        ) });
                    Counter("flutterperf.hints_emitted").Increment(1);
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

        var json = JsonSerializer.Serialize(payload);
        await cards.BroadcastAsync(new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "FlutterPerfCard",
        DataJson:           json) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: FlutterPerfNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: now
        ) });
        Counter("flutterperf.cards_broadcast").Increment(1);
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
