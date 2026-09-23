using System.Collections.Concurrent;
using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

internal sealed class InMemoryAppObservationStore : IAppObservationStore
{
    private readonly ConcurrentDictionary<string, MutableObservation> observations = new(StringComparer.Ordinal);

    public void Record(string appId, IEnumerable<string> dataClasses, IEnumerable<string> meters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var observation = observations.GetOrAdd(appId, _ => new MutableObservation());
        observation.Add(dataClasses, meters);
    }

    public AppObservation Observe(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        return observations.TryGetValue(appId, out var observation)
            ? observation.Snapshot(appId)
            : new AppObservation { AppId = appId };
    }

    public DeclaredObservedDiff Diff(AppManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return DeclaredObservedDiff.Compute(manifest, Observe(manifest.Id));
    }

    private sealed class MutableObservation
    {
        private readonly Lock gate = new();
        private readonly HashSet<string> dataClasses = new(StringComparer.Ordinal);
        private readonly HashSet<string> meters = new(StringComparer.Ordinal);
        private int calls;

        public void Add(IEnumerable<string> usedDataClasses, IEnumerable<string> usedMeters)
        {
            lock (gate)
            {
                dataClasses.UnionWith(usedDataClasses);
                meters.UnionWith(usedMeters);
                calls++;
            }
        }

        public AppObservation Snapshot(string appId)
        {
            lock (gate)
            {
                return new AppObservation
                {
                    AppId = appId,
                    DataClasses = new HashSet<string>(dataClasses, StringComparer.Ordinal),
                    Meters = new HashSet<string>(meters, StringComparer.Ordinal),
                    Calls = calls,
                };
            }
        }
    }
}
