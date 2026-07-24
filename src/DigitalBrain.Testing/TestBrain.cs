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
    private readonly string _scope;
    private readonly TestOwner _defaultOwner;
    private Action? _release;

    private TestBrain(
        FixtureCluster cluster,
        string scope,
        TestClock clock,
        Action release)
    {
        Cluster = cluster;
        _scope = scope;
        _release = release;
        Clock = clock;

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
        Action release)
        => new(cluster, scope, clock, release);

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
        => _defaultOwner.Neuron<TNeuron>(name);

    public TestOwner Owner(string label)
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
            return owner;
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

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(
                "One or more DigitalBrain test resources failed to clean up.",
                failures);
        }
    }

    internal JournalFaultHandle ArmJournalFault(
        NeuronId target,
        int completedWrites,
        string message)
    {
        lock (_faultGate)
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
                RetireJournalFault);
            _faults.Add(handle);
            return handle;
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
                journal = new TestJournal(Cluster, subject, direction);
                _journals.Add(key, journal);
            }

            return journal;
        }
    }

    internal Task RestartHostAsync(
        NeuronId target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _release) is null,
            this);
        return Cluster.RestartHostAsync(target, cancellationToken);
    }

    private TestOwner CreateOwner(string label)
        => TestOwner.Create(
            this,
            new($"{_scope}-{label}"));

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
