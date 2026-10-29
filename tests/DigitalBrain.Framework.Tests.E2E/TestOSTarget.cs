using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E;

/// <summary>TestOS: no actor framework, the clock moves only when told (04-08).</summary>
public sealed class TestOSTarget : FrameworkTarget
{
    public const string TargetName = "testos";

    public override string Name => TargetName;

    public override Task<RunningTarget> StartHost(FrameworkHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var os = TestOS.Local(o =>
        {
            if (options.HistoryWindowSignals is { } signals) { o.HistoryWindowSignals = signals; }
            if (options.HistoryWindowAge is { } age) { o.HistoryWindowAge = age; }
            if (options.StaleCursorAfter is { } stale) { o.StaleCursorAfter = stale; }
            o.DuplicateDelivery = options.DuplicateDelivery;
        }).Register(typeof(FrameworkModule).Assembly);
        return Task.FromResult<RunningTarget>(new Run(os));
    }

    private sealed class Run(TestOS os) : RunningTarget
    {
        public override T Get<T>(NeuronId id) => os.Get<T>(id);

        public override Task Deactivate(NeuronId id) => os.Deactivate(id);

        public override Task Restart(CancellationToken ct) => os.Restart();

        public override void FailNextPublishAfterAppend(NeuronId publisher) => os.FailNextPublishAfterAppend(publisher);

        public override void Advance(TimeSpan by) => os.Clock.Advance(by);

        public override Task ForceCheckpoint(BrainId brain, CancellationToken ct)
        {
            os.ForceCheckpoint(brain);
            return Task.CompletedTask;
        }

        public override Task Idle() => os.Idle();

        public override Task<int> SubscriptionCount(NeuronId subscriber, NeuronId source, Type signal)
            => Task.FromResult(os.SynapsesOf(subscriber).Count(synapse => synapse.From == source && synapse.Signal == signal));

        public override ValueTask DisposeAsync() => os.DisposeAsync();
    }
}
