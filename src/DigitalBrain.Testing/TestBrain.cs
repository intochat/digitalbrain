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
    private readonly string _scope;
    private readonly TestOwner _defaultOwner;
    private Action? _release;

    private TestBrain(FixtureCluster cluster, string scope, Action release)
    {
        Cluster = cluster;
        _scope = scope;
        _release = release;

        var owner = CreateOwner(DefaultOwnerLabel);
        _labelSpellings.Add(DefaultOwnerLabel, DefaultOwnerLabel);
        _owners.Add(DefaultOwnerLabel, owner);
        _defaultOwner = owner;
        Client = owner.Client;
    }

    public IDigitalBrain Client { get; }

    internal FixtureCluster Cluster { get; }

    internal static TestBrain Create(
        FixtureCluster cluster,
        string scope,
        Action release)
        => new(cluster, scope, release);

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

    public async ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        if (release is null)
        {
            return;
        }

        try
        {
            await DisposeJournalsAsync();
        }
        finally
        {
            release();
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

    private TestOwner CreateOwner(string label)
        => TestOwner.Create(
            this,
            new($"{_scope}-{label}"));

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
