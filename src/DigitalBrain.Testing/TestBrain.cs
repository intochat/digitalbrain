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
    private readonly string _scope;
    private Action? _release;

    private TestBrain(FixtureCluster cluster, string scope, Action release)
    {
        Cluster = cluster;
        _scope = scope;
        _release = release;

        var owner = CreateOwner(DefaultOwnerLabel);
        _labelSpellings.Add(DefaultOwnerLabel, DefaultOwnerLabel);
        _owners.Add(DefaultOwnerLabel, owner);
        Client = owner.Client;
    }

    public IDigitalBrain Client { get; }

    internal FixtureCluster Cluster { get; }

    internal static TestBrain Create(
        FixtureCluster cluster,
        string scope,
        Action release)
        => new(cluster, scope, release);

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

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
        return ValueTask.CompletedTask;
    }

    private TestOwner CreateOwner(string label)
        => TestOwner.Create(
            Cluster,
            new($"{_scope}-{label}"));
}
