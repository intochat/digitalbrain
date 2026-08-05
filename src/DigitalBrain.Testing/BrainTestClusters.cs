using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Testing;

// The assembly fixture owning every composed cluster: one ComposedFixture per
// Compose-declaring type, fingerprint-pinned so a type can never serve two different
// compositions. Declared once per test assembly:
// [assembly: AssemblyFixture(typeof(BrainTestClusters))]. Teardown stops every booted
// cluster and then fails loudly on any journal commit fault that was armed but never
// consumed — leaked test intent never passes silently.
public sealed class BrainTestClusters : IAsyncLifetime
{
    private readonly Lock gate = new();
    private readonly Dictionary<Type, ComposedFixture> byComposition = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Every composed cluster is torn down before the first failure is rethrown.")]
    public async ValueTask DisposeAsync()
    {
        ComposedFixture[] fixtures;
        lock (gate)
        {
            fixtures = [.. byComposition.Values];
            byComposition.Clear();
        }

        List<Exception>? failures = null;
        List<string>? leakedFaults = null;
        foreach (var fixture in fixtures)
        {
            try
            {
                await fixture.SettleAsync();
                var unconsumed = fixture.UnconsumedFaults();
                if (unconsumed.Count > 0)
                {
                    (leakedFaults ??= []).AddRange(unconsumed);
                }

                await fixture.DisposeAsync();
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

        if (leakedFaults is not null)
        {
            throw new InvalidOperationException(
                "Unconsumed journal commit faults remain: " + string.Join("; ", leakedFaults));
        }
    }

    internal ComposedFixture FixtureFor(Type composition, Action<DigitalBrainTestBuilder> compose)
    {
        lock (gate)
        {
            if (byComposition.TryGetValue(composition, out var existing))
            {
                var requested = ComposedFixture.FingerprintOf(compose);
                if (!string.Equals(requested, existing.Fingerprint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Composition '{composition}' is already bound to '{existing.Fingerprint}', so it "
                        + $"cannot also serve '{requested}'. Declare the differing composition on its own type.");
                }

                return existing;
            }

            var fixture = new ComposedFixture(compose);
            byComposition.Add(composition, fixture);
            return fixture;
        }
    }
}
