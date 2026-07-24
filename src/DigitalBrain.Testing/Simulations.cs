using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public static class Simulations
{
    public static async Task<Scenario> OpenAsync(CancellationToken cancellationToken = default)
    {
        await SimulationClusterHost.EnsureStartedAsync(cancellationToken);

        return new Scenario(
            owner: new OwnerId(Guid.NewGuid().ToString("N")),
            clock: new ScenarioClock(),
            grains: SimulationClusterHost.Grains);
    }
}
