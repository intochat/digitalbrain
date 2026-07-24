using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestBrain : IAsyncDisposable
{
    private const string DefaultOwnerLabel = "default";

    private readonly Lock _ownerGate = new();
    private readonly Dictionary<string, string> _labelSpellings =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TestOwner> _owners =
        new(StringComparer.Ordinal);
    private readonly Lock _journalGate = new();
    private readonly Dictionary<(NeuronId Subject, JournalKind Direction), TestJournal>
        _journals = [];
    private readonly Lock _faultGate = new();
    private readonly HashSet<JournalFaultHandle> _faults = [];
    private readonly BrainTestDiagnostics _diagnostics;
    private readonly TestEdgeRegistry _edges;
    private readonly long _edgeGeneration;
    private readonly string _scope;
    private readonly TestOwner _defaultOwner;
    private Action? _release;

    private TestBrain(
        FixtureCluster cluster,
        string scope,
        TestClock clock,
        BrainTestDiagnostics diagnostics,
        TestEdgeRegistry edges,
        long edgeGeneration,
        Action release)
    {
        Cluster = cluster;
        _scope = scope;
        _release = release;
        Clock = clock;
        _diagnostics = diagnostics;
        _edges = edges;
        _edgeGeneration = edgeGeneration;

        var owner = CreateOwner(DefaultOwnerLabel);
        _labelSpellings.Add(DefaultOwnerLabel, DefaultOwnerLabel);
        _owners.Add(DefaultOwnerLabel, owner);
        _defaultOwner = owner;
        Client = owner.Client;
    }

    public IDigitalBrain Client { get; }

    public TestClock Clock { get; }

    internal FixtureCluster Cluster { get; }

    internal static TestBrain Create(
        FixtureCluster cluster,
        string scope,
        TestClock clock,
        BrainTestDiagnostics diagnostics,
        TestEdgeRegistry edges,
        long edgeGeneration,
        Action release)
        => new(
            cluster,
            scope,
            clock,
            diagnostics,
            edges,
            edgeGeneration,
            release);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TScript ChatClientScript<TScript>()
        where TScript : class
    {
        ThrowIfDisposed();
        return _edges.Script<TScript>(
            TestEdgeKind.ChatClient,
            _edgeGeneration);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TScript SouthboundMcpTransportScript<TScript>()
        where TScript : class
    {
        ThrowIfDisposed();
        return _edges.Script<TScript>(
            TestEdgeKind.SouthboundMcpTransport,
            _edgeGeneration);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SetOAuthParameter(
        string name,
        string? value)
    {
        ThrowIfDisposed();
        _edges.SetOAuthParameter(
            name,
            value,
            _edgeGeneration);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? OAuthParameter(string name)
    {
        ThrowIfDisposed();
        return _edges.OAuthParameter(
            name,
            _edgeGeneration);
    }

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
        => _defaultOwner.Neuron<TNeuron>(name);

    public TestOwner Owner(string label)
    {
        try
        {
            var validated = IdentityLabel.Validate(label);

            lock (_ownerGate)
            {
                if (_owners.TryGetValue(validated, out var owner))
                {
                    return owner;
                }

                if (_labelSpellings.TryGetValue(validated, out var existing))
                {
                    throw new ArgumentException(
                        $"Owner label '{validated}' differs only by casing from already used label '{existing}'.",
                        nameof(label));
                }

                owner = CreateOwner(validated);
                _labelSpellings.Add(validated, validated);
                _owners.Add(validated, owner);
                _diagnostics.RecordEvent(
                    "owner.open",
                    "succeeded",
                    ("label", validated),
                    ("owner", owner.Id.Value));
                return owner;
            }
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure(
                "owner.open",
                failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Method teardown must disarm every journal fault and attempt every journal cleanup before releasing the serial fixture lease; all failures are preserved.")]
    public async ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        if (release is null)
        {
            return;
        }

        List<Exception> failures = [];
        InvalidOperationException? faultCleanupFailure = null;

        try
        {
            try
            {
                await Clock.InvalidateAsync();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }

            if (CleanupJournalFaults() is { } faultFailure)
            {
                faultCleanupFailure = faultFailure;
                failures.Add(faultFailure);
            }

            try
            {
                await DisposeJournalsAsync();
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        finally
        {
            release();
        }

        if (failures.Count > 0)
        {
            var failure = failures.Count == 1
                ? failures[0]
                : new AggregateException(
                    "One or more DigitalBrain test resources failed to clean up.",
                    failures);
            var cleanupStage = failures.Count == 1
                && ReferenceEquals(
                    failure,
                    faultCleanupFailure)
                    ? "fault-cleanup"
                    : "method-cleanup";

            throw _diagnostics.CaptureFailure(
                "brain.cleanup",
                failure,
                cleanupStage);
        }
    }

    internal JournalFaultHandle ArmJournalFault(
        NeuronId target,
        int completedWrites,
        string message)
    {
        lock (_faultGate)
        {
            try
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref _release) is null,
                    this);
                var registration = Cluster.ArmJournalFault(
                    target,
                    completedWrites,
                    message);
                var handle = new JournalFaultHandle(
                    registration,
                    RetireJournalFault,
                    _diagnostics);
                _faults.Add(handle);
                _diagnostics.TrackFault(handle, target.ToString());
                return handle;
            }
            catch (Exception failure)
                when (failure is not BrainTestFailureException)
            {
                throw _diagnostics.CaptureFailure(
                    "fault.arm",
                    failure);
            }
        }
    }

    internal TestJournal Journal(NeuronId subject, JournalKind direction)
    {
        lock (_journalGate)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _release) is null,
                this);

            var key = (subject, direction);
            if (!_journals.TryGetValue(key, out var journal))
            {
                journal = new TestJournal(
                    Cluster,
                    subject,
                    direction,
                    _diagnostics);
                _journals.Add(key, journal);
            }

            return journal;
        }
    }

    internal async Task RestartHostAsync(
        NeuronId target,
        CancellationToken cancellationToken)
    {
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _release) is null,
                this);
            _diagnostics.RecordEvent(
                "neuron.restart",
                "started",
                ("target", target.ToString()));
            await Cluster.RestartHostAsync(target, cancellationToken);
            _diagnostics.RecordEvent(
                "neuron.restart",
                "succeeded",
                ("target", target.ToString()));
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure(
                "neuron.restart",
                failure);
        }
    }

    private TestOwner CreateOwner(string label)
    {
        var owner = TestOwner.Create(
            this,
            new($"{_scope}-{label}"));
        _diagnostics.RecordOwner(label, owner.Id.Value);
        return owner;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _release) is null,
            this);

    internal BrainTestFailureException CaptureFailure(
        string operation,
        Exception failure)
        => _diagnostics.CaptureFailure(operation, failure);

    internal void RecordNeuron(NeuronId target)
        => _diagnostics.RecordEvent(
            "neuron.open",
            "succeeded",
            ("target", target.ToString()));

    private bool RetireJournalFault(JournalFaultHandle fault)
    {
        lock (_faultGate)
        {
            _faults.Remove(fault);
            var disarmed =
                Cluster.DisarmJournalFault(fault.Registration);
            _diagnostics.RetireFault(
                fault,
                disarmed ? "succeeded" : "inactive");
            return disarmed;
        }
    }

    private InvalidOperationException? CleanupJournalFaults()
    {
        JournalFaultHandle[] faults;

        lock (_faultGate)
        {
            faults = [.. _faults];
            _faults.Clear();
        }

        List<string>? leaks = null;

        foreach (var fault in faults)
        {
            if (fault.IsConsumed)
            {
                continue;
            }

            if (!fault.Disarm() || fault.IsConsumed)
            {
                continue;
            }

            _diagnostics.RecordCleanupLeak(fault);
            (leaks ??= []).Add(
                $"neuron='{fault.Target}', message='{fault.Message}'");
        }

        return leaks is null
            ? null
            : new InvalidOperationException(
                $"Unconsumed journal commit faults were not explicitly disposed before method cleanup: {string.Join("; ", leaks)}.");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Method teardown must attempt every journal cleanup before releasing the serial fixture lease; all failures are preserved in an aggregate.")]
    private async Task DisposeJournalsAsync()
    {
        TestJournal[] journals;

        lock (_journalGate)
        {
            journals = [.. _journals.Values];
            _journals.Clear();
        }

        List<Exception>? failures = null;

        foreach (var journal in journals)
        {
            try
            {
                await journal.DisposeAsync();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more DigitalBrain test journals failed to clean up.",
                failures);
        }
    }
}
