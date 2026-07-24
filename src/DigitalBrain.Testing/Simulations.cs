using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public static class Simulations
{
    public static Task<Scenario> OpenAsync(CancellationToken cancellationToken = default)
        => OpenAsync(new OwnerId(Guid.NewGuid().ToString("N")), cancellationToken);

    public static Task<Scenario> OpenAsync(string owner, CancellationToken cancellationToken = default)
        => OpenAsync(new OwnerId(owner), cancellationToken);

    public static async Task<Scenario> OpenAsync(OwnerId owner, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner.Value))
        {
            throw new ArgumentException("Owner must be a non-empty value.", nameof(owner));
        }

        await SimulationClusterHost.EnsureStartedAsync(cancellationToken);

        return new Scenario(
            owner: owner,
            clock: SimulationClusterHost.Clock,
            grains: SimulationClusterHost.Grains);
    }
}
