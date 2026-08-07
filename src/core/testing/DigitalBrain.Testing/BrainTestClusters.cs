using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Testing;

public sealed class BrainTestClusters : IAsyncLifetime
{
    private static BrainTestClusters? _registered;

    private readonly Lock _gate = new();
    private readonly Dictionary<Type, ComposedFixture> _byComposition = [];

    public int BootedClusters
    {
        get
        {
            lock (_gate)
            {
                return _byComposition.Values.Count(fixture => fixture.HasBooted);
            }
        }
    }

    public bool HasBootedCluster(Type composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        lock (_gate)
        {
            return _byComposition.TryGetValue(composition, out var fixture) && fixture.HasBooted;
        }
    }

    public ValueTask InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _registered, this, null) is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(BrainTestClusters)} is registered more than once in this test assembly.");
        }

        return ValueTask.CompletedTask;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every composed cluster is torn down before the first failure is rethrown.")]
    public async ValueTask DisposeAsync()
    {
        Interlocked.CompareExchange(ref _registered, null, this);

        ComposedFixture[] fixtures;
        lock (_gate)
        {
            fixtures = [.. _byComposition.Values];
            _byComposition.Clear();
        }

        List<Exception>? failures = null;
        foreach (var fixture in fixtures)
        {
            try
            {
                await fixture.SettleAsync().ConfigureAwait(false);
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more composed DigitalBrain test clusters failed to stop.", failures);
        }
    }

    internal static BrainTestClusters Registered
        => Volatile.Read(ref _registered)
            ?? throw new InvalidOperationException(
                "Tests deriving from DigitalBrainTest or NeuronTest need "
                + "[assembly: AssemblyFixture(typeof(BrainTestClusters))] in the test assembly.");

    internal Task<TestBrain> LeaseAsync(
        Type composition,
        Action<DigitalBrainTestBuilder> compose,
        CancellationToken cancellationToken)
        => FixtureFor(composition, compose).LeaseAsync(cancellationToken);

    internal ComposedFixture FixtureFor(Type composition, Action<DigitalBrainTestBuilder> compose)
    {
        lock (_gate)
        {
            if (_byComposition.TryGetValue(composition, out var existing))
            {
                var requested = ComposedFixture.FingerprintOf(compose);
                if (!string.Equals(requested, existing.Fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Composition '{composition}' is already bound to '{existing.Fingerprint}', "
                        + $"so it cannot also serve '{requested}'. Declare the differing composition on its own type.");
                }
            }
            else
            {
                existing = new ComposedFixture(compose);
                _byComposition.Add(composition, existing);
            }

            return existing;
        }
    }
}
