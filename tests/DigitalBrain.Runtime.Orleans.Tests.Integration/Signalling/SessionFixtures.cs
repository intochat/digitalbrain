using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

/// <summary>A synapse silo whose sessions and listeners check SessionTtl every 200 ms.</summary>
public class SessionSilo : SynapseSilo
{
    protected override IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>
    {
        [RuntimeOptions.IntoChatCompatible.SignallingSection + ":SessionSweepEvery"] = "00:00:00.200",
    };
}

/// <summary>Its own silo: facts that move the runtime clock past SessionTtl.</summary>
public sealed class SessionTtlSilo : SessionSilo;

internal static class Sessions
{
    public static async Task Until(Func<Task<bool>> condition, CancellationToken ct, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!await condition() && DateTime.UtcNow < deadline) { await Task.Delay(100, ct); }
    }
}
