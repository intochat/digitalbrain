using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainTest(BrainTestClusters clusters) : IAsyncLifetime
{
    private const BindingFlags DeclaredCompose =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

    private static readonly Type[] ComposeSignature = [typeof(DigitalBrainTestBuilder)];

    private static readonly MethodInfo ComposeSlot = typeof(DigitalBrainTest).GetMethod(
        nameof(Compose), DeclaredCompose, binder: null, ComposeSignature, modifiers: null)!;

    private static readonly ConcurrentDictionary<Type, Type> CompositionKeys = new();

    private static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollBackoff = TimeSpan.FromMilliseconds(50);

    private readonly List<JournalFaultHandle> faultHandles = [];

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    protected Brain Brain => Fixture.Brain;

    protected TestClock Clock => Fixture.Clock;

    protected virtual void Compose(DigitalBrainTestBuilder brain)
    {
    }

    public async ValueTask InitializeAsync()
        => Fixture = await clusters
            .FixtureFor(CompositionOf(GetType()), Compose)
            .LeaseAsync(Cancellation);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        List<string>? leaked = null;
        foreach (var handle in faultHandles)
        {
            _ = handle.Disarm();
            if (!handle.IsConsumed)
            {
                (leaked ??= []).Add($"{handle.Target}: {handle.Message}");
            }
        }

        faultHandles.Clear();
        return leaked is null
            ? ValueTask.CompletedTask
            : throw new InvalidOperationException(
                "Unconsumed journal commit faults remain: " + string.Join("; ", leaked));
    }

    protected Task<NeuronReading> ReadAsync(NeuronId neuron, CancellationToken cancellationToken = default)
        => Brain.ReadAsync(neuron, 0, cancellationToken);

    protected async Task<TFact> WaitForAsync<TFact>(NeuronId neuron, CancellationToken cancellationToken = default)
        where TFact : Synapse
    {
        var reading = await WaitForJournalAsync(
            neuron,
            observed => observed.Journal.Any(fact => fact.Body is TFact),
            $"a {typeof(TFact).Name} body",
            cancellationToken);

        for (var index = reading.Journal.Count - 1; index >= 0; index--)
        {
            if (reading.Journal[index].Body is TFact fact)
            {
                return fact;
            }
        }

        throw new UnreachableException($"The awaited {typeof(TFact).Name} vanished from a committed journal.");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A poll attempt against a poisoned or mid-deactivation neuron throws by design; the bound reports the last failure if the wait never succeeds.")]
    protected async Task<NeuronReading> WaitForJournalAsync(
        NeuronId neuron,
        Func<NeuronReading, bool> holds,
        string expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;
        IReadOnlyList<JournalFact>? lastJournal = null;

        while (stopwatch.Elapsed < WaitBound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reading = await Brain.ReadAsync(neuron, 0, cancellationToken);
                lastJournal = reading.Journal;
                lastFailure = null;
                if (holds(reading))
                {
                    return reading;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                lastFailure = failure;
            }

            await Task.Delay(PollBackoff, cancellationToken);
        }

        var observed = lastJournal is null
            ? lastFailure is null ? "no read ever completed" : $"the last read failed: {lastFailure.Message}"
            : $"the journal holds [{string.Join(", ", lastJournal.Select(fact => $"{fact.Entry} {fact.Kind}"))}]";
        throw new TimeoutException(
            $"{neuron} did not journal {expectation} within {WaitBound.TotalSeconds:F0}s; {observed}.");
    }

    protected JournalFaultHandle FailNextJournalCommit(
        NeuronId neuron, int allowCommitsBeforeFault = 0, bool stickyUntilDisarm = false)
    {
        var registration = Fixture.ArmFault(
            neuron,
            $"Injected journal commit fault on {neuron}.",
            allowCommitsBeforeFault,
            stickyUntilDisarm);
        var handle = new JournalFaultHandle(registration, armed => Fixture.DisarmFault(armed.Registration));
        faultHandles.Add(handle);
        return handle;
    }

    protected Task DeactivateAsync(IReadOnlyList<NeuronId> neurons, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(neurons);
        return Fixture.DeactivateAsync(neurons, cancellationToken);
    }

    private ComposedFixture Fixture
    {
        get => field ?? throw new InvalidOperationException(
            $"The test has not been initialized; {nameof(DigitalBrainTest)} leases its cluster in "
            + $"{nameof(InitializeAsync)} — is [assembly: AssemblyFixture(typeof(BrainTestClusters))] declared?");
        set;
    }

    private static Type CompositionOf(Type test)
        => CompositionKeys.GetOrAdd(test, static candidate =>
        {
            for (var declaring = candidate; declaring is not null; declaring = declaring.BaseType)
            {
                var declared = declaring.GetMethod(
                    nameof(Compose), DeclaredCompose, binder: null, ComposeSignature, modifiers: null);
                if (declared is not null && declared.GetBaseDefinition().Equals(ComposeSlot))
                {
                    return declaring;
                }
            }

            throw new UnreachableException(
                $"{candidate} derives from {nameof(DigitalBrainTest)} but declares no composition slot.");
        });
}
