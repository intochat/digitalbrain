using DigitalBrain.Kernel;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Orleans.Storage;

internal sealed class UnregisteredJournal : IBrainJournal, IJournalView
{
    public static readonly UnregisteredJournal Instance = new();

    public BrainId Brain => throw NotConfigured();

    public Position Last => new(0);

    public SubscriptionIndex Subscriptions { get; } = new();

    public Position FloorOf(NeuronId? publisher) => new(0);

    public Position LastOf(NeuronId publisher) => new(0);

    public IEnumerable<AliasMark> Marks(NeuronId? publisher) => [];

    public long SequenceAt(NeuronId publisher, Position position) => 0;

    public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
        => Task.FromResult(new JournalPage(new(0), new(0), []));

    public Task<CommitResult> Append(Commit commit, CancellationToken ct = default) => throw NotConfigured();

    public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
        => Task.FromResult<JournalRecord?>(null);

    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default) => throw NotConfigured();

    public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default) => throw NotConfigured();

    public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default) => throw NotConfigured();

    public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default) => throw NotConfigured();

    private static InvalidOperationException NotConfigured()
        => new("The silo has no brain storage registered. Call AddBrainStorage or configure a topology that provides it.");
}
