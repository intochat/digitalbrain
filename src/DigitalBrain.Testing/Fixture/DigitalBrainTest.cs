using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Xunit;

namespace DigitalBrain.Testing;

public abstract class DigitalBrainTest(DigitalBrainTestClusters clusters) : IAsyncLifetime
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

    protected TestClock Clock => Fixture.Clock;

    protected virtual void Compose(DigitalBrainTestBuilder composition)
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
                "Unconsumed journal recording faults remain: " + string.Join("; ", leaked));
    }

    protected Task PublishAsync(string source, Synapse synapse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        return Fixture
            .OpenDefaultWorkspace(source, synapse.GetType())
            .Publisher
            .PublishAsync(synapse, cancellationToken);
    }

    protected WorkspaceChannel OpenWorkspace(
        string scope,
        string source,
        params Type[] permittedIngressSynapses)
        => Fixture.OpenWorkspace(scope, source, permittedIngressSynapses);

    protected bool HasAmbientAccessServices() => Fixture.HasAmbientAccessServices();

    protected Task<JournalRead> ReadOutcomeAsync(
        NeuronId neuron,
        long afterPosition = 0,
        int maximumRecords = 256,
        CancellationToken cancellationToken = default)
        => ReadOutcomeAsync(Fixture.DefaultWorkspace, neuron, afterPosition, maximumRecords, cancellationToken);

    protected static Task<JournalRead> ReadOutcomeAsync(
        WorkspaceChannel workspace,
        NeuronId neuron,
        long afterPosition = 0,
        int maximumRecords = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return workspace.Journal.ReadAsync(neuron, afterPosition, maximumRecords, cancellationToken);
    }

    protected async Task<JournalPage> ReadAsync(
        NeuronId neuron,
        long afterPosition = 0,
        int maximumRecords = 256,
        CancellationToken cancellationToken = default)
    {
        var read = await ReadOutcomeAsync(neuron, afterPosition, maximumRecords, cancellationToken);
        return read as JournalPage
            ?? throw new InvalidOperationException($"{neuron} returned unavailable journal history in a test without retention.");
    }

    protected static async Task<JournalPage> ReadAsync(
        WorkspaceChannel workspace,
        NeuronId neuron,
        long afterPosition = 0,
        int maximumRecords = 256,
        CancellationToken cancellationToken = default)
    {
        var read = await ReadOutcomeAsync(workspace, neuron, afterPosition, maximumRecords, cancellationToken);
        return read as JournalPage
            ?? throw new InvalidOperationException($"{neuron} returned unavailable journal history in a test without retention.");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A poll attempt against a poisoned or mid-deactivation host can throw; the bound reports the last failure if the wait never succeeds.")]
    protected async Task<JournalPage> WaitForJournalAsync(
        NeuronId neuron,
        Func<JournalPage, bool> holds,
        string expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;
        IReadOnlyList<JournalRecord>? lastRecords = null;

        while (stopwatch.Elapsed < WaitBound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await ReadAsync(neuron, cancellationToken: cancellationToken);
                lastRecords = page.Records;
                lastFailure = null;
                if (holds(page))
                {
                    return page;
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

        var observed = lastRecords is null
            ? lastFailure is null ? "no read ever completed" : $"the last read failed: {lastFailure.Message}"
            : $"the journal holds [{string.Join(", ", lastRecords.Select(record => $"{record.Direction} {record.SynapseKind}"))}]";
        throw new TimeoutException(
            $"{neuron} did not record {expectation} within {WaitBound.TotalSeconds:F0}s; {observed}.");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A poll attempt against a scoped host can throw while it reloads; the bound reports the last failure if the wait never succeeds.")]
    protected static async Task<JournalPage> WaitForJournalAsync(
        WorkspaceChannel workspace,
        NeuronId neuron,
        Func<JournalPage, bool> holds,
        string expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(holds);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectation);

        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;
        IReadOnlyList<JournalRecord>? lastRecords = null;

        while (stopwatch.Elapsed < WaitBound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await ReadAsync(workspace, neuron, cancellationToken: cancellationToken);
                lastRecords = page.Records;
                lastFailure = null;
                if (holds(page))
                {
                    return page;
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

        var observed = lastRecords is null
            ? lastFailure is null ? "no read ever completed" : $"the last read failed: {lastFailure.Message}"
            : $"the journal holds [{string.Join(", ", lastRecords.Select(record => $"{record.Direction} {record.SynapseKind}"))}]";
        throw new TimeoutException(
            $"{neuron} did not record {expectation} within {WaitBound.TotalSeconds:F0}s; {observed}.");
    }

    protected JournalFaultHandle FailNextJournalRecording(
        NeuronId neuron, int allowRecordingsBeforeFault = 0, bool stickyUntilDisarm = false)
    {
        var registration = Fixture.ArmFault(
            neuron,
            $"Injected journal recording fault on {neuron}.",
            allowRecordingsBeforeFault,
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

    protected Task DeactivateAsync(
        string workspaceScope,
        IReadOnlyList<NeuronId> neurons,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceScope);
        ArgumentNullException.ThrowIfNull(neurons);
        return Fixture.DeactivateAsync(workspaceScope, neurons, cancellationToken);
    }

    protected Task DrainAsync(NeuronId neuron, CancellationToken cancellationToken = default)
        => Fixture.DrainAsync(neuron, cancellationToken);

    protected Task DrainAsync(string workspaceScope, NeuronId neuron, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceScope);
        return Fixture.DrainAsync(workspaceScope, neuron, cancellationToken);
    }

    private ComposedFixture Fixture
    {
        get => field ?? throw new InvalidOperationException(
            $"The test has not been initialized; {nameof(DigitalBrainTest)} leases its cluster in "
            + $"{nameof(InitializeAsync)} — is [assembly: AssemblyFixture(typeof(DigitalBrainTestClusters))] declared?");
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
