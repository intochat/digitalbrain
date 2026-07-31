using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestBrain : IAsyncDisposable
{
    private const string DefaultOwnerLabel = "default";

    private readonly Lock _ownerGate = new();
    private readonly Dictionary<string, TestOwner> _owners =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _ownerLabels =
        new(StringComparer.OrdinalIgnoreCase);
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

    internal TestBrain(
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

        _defaultOwner = CreateOwner(DefaultOwnerLabel);
        _owners.Add(DefaultOwnerLabel, _defaultOwner);
        _ownerLabels.Add(DefaultOwnerLabel);
        Client = _defaultOwner.Client;
    }

    public IDigitalBrain Client { get; }

    public TestClock Clock { get; }

    internal FixtureCluster Cluster { get; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TScript ChatClientScript<TScript>()
        where TScript : class
    {
        ThrowIfDisposed();
        return _edges.ChatClientScript<TScript>(_edgeGeneration);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TScript ServiceEdgeScript<TScript>()
        where TScript : class
    {
        ThrowIfDisposed();
        return _edges.ServiceEdgeScript<TScript>(_edgeGeneration);
    }

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
        => _defaultOwner.Neuron<TNeuron>(name);

    public TestOwner Owner(string label)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            if (label.Contains('/', StringComparison.Ordinal)
                || label.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException(
                    "Owner labels cannot contain '/' or whitespace.",
                    nameof(label));
            }

            lock (_ownerGate)
            {
                if (_owners.TryGetValue(label, out var owner))
                {
                    return owner;
                }

                if (!_ownerLabels.Add(label))
                {
                    throw new ArgumentException(
                        $"Owner label '{label}' differs only by casing from an existing label.",
                        nameof(label));
                }

                owner = CreateOwner(label);
                _owners.Add(label, owner);
                return owner;
            }
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("owner.open", failure);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Teardown attempts every cleanup step before releasing the fixture lease.")]
    public async ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        if (release is null)
        {
            return;
        }

        List<Exception> failures = [];

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
            throw _diagnostics.CaptureFailure("brain.cleanup", failure);
        }
    }

    internal JournalFaultHandle ArmJournalFault(
        NeuronId target,
        string message,
        int allowCommitsBeforeFault = 0)
    {
        lock (_faultGate)
        {
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
                var registration = Cluster.ArmJournalFault(target, message, allowCommitsBeforeFault);
                var handle = new JournalFaultHandle(registration, RetireJournalFault, _diagnostics);
                _faults.Add(handle);
                return handle;
            }
            catch (Exception failure) when (failure is not BrainTestFailureException)
            {
                throw _diagnostics.CaptureFailure("fault.arm", failure);
            }
        }
    }

    internal Task<bool> HasOutboxWakeupAsync(NeuronId neuron)
    {
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            return Cluster.HasOutboxWakeupAsync(neuron);
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("outbox.wakeup", failure);
        }
    }

    internal TestJournal Journal(NeuronId subject, JournalKind direction)
    {
        lock (_journalGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);

            var key = (subject, direction);
            if (!_journals.TryGetValue(key, out var journal))
            {
                journal = new TestJournal(Cluster, subject, direction, _diagnostics);
                _journals.Add(key, journal);
            }

            return journal;
        }
    }

    internal async Task RestartHostAsync(NeuronId target, CancellationToken cancellationToken)
    {
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            await Cluster.RestartHostAsync(target, cancellationToken);
        }
        catch (Exception failure) when (failure is not BrainTestFailureException)
        {
            throw _diagnostics.CaptureFailure("neuron.restart", failure);
        }
    }

    private TestOwner CreateOwner(string label)
        => new(this, new($"{_scope}-{label}"));

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);

    internal BrainTestFailureException CaptureFailure(string operation, Exception failure)
        => _diagnostics.CaptureFailure(operation, failure);

    private bool RetireJournalFault(JournalFaultHandle fault)
    {
        lock (_faultGate)
        {
            _faults.Remove(fault);
            return Cluster.DisarmJournalFault(fault.Registration);
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

            (leaks ??= []).Add($"neuron='{fault.Target}', message='{fault.Message}'");
        }

        return leaks is null
            ? null
            : new InvalidOperationException(
                $"Unconsumed journal commit faults remain: {string.Join("; ", leaks)}.");
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every journal cleanup is attempted before releasing the fixture lease.")]
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
                "One or more DigitalBrain test journals failed to clean up.", failures);
        }
    }
}
