using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;
using Orleans.Metadata;

namespace DigitalBrain.Microsoft.Aspire;

// Keyed by the AppHost application name. Commands travel to the AppHost over its bridge and interleave,
// so the state reports a slow start produces are not queued behind it.
[Alias("microsoft.aspire"), DefaultGrainType("microsoft.aspire")]
public interface IAspire : INeuron
{
    Task<IReadOnlyList<AspireResource>> ListResources(CancellationToken cancellationToken = default);
    [AlwaysInterleave, ResponseTimeout("00:05:00")] Task StartResource(string name, CancellationToken cancellationToken = default);
    [AlwaysInterleave, ResponseTimeout("00:05:00")] Task StopResource(string name, CancellationToken cancellationToken = default);
    [AlwaysInterleave, ResponseTimeout("00:05:00")] Task RestartResource(string name, CancellationToken cancellationToken = default);
}
