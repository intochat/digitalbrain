using DigitalBrain.Supabase;
using Orleans;

namespace DigitalBrain.Connections;

// Production probe. Supabase is validated through the module's read-only connection read; every
// other source accepts a non-empty stored credential. No probe writes to the remote source.
internal sealed class CompositeConnectionProbe(IGrainFactory grains) : IConnectionProbe
{
    public async Task<ConnectionProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
    {
        if (string.Equals(source, ConnectionSources.Supabase, StringComparison.Ordinal))
        {
            try
            {
                var connection = await grains.GetGrain<ISupabase>(SupabaseNames.DefaultNeuron).ReadConnection();
                return connection.Connected
                    ? ConnectionProbeResult.Connected($"Supabase '{connection.Database}' answered a read-only probe.")
                    : ConnectionProbeResult.Failing("Supabase did not answer the read-only probe.");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return ConnectionProbeResult.Failing("The read-only Supabase probe failed.");
            }
        }

        return string.IsNullOrWhiteSpace(credential)
            ? ConnectionProbeResult.Failing("No credential is stored for this connection.")
            : ConnectionProbeResult.Connected();
    }
}