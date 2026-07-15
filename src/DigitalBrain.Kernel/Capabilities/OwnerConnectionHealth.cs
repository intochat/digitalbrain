using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Capabilities;

public interface IOwnerConnectionHealth
{
    Task<IReadOnlySet<CapabilityConnectionBinding>> ReadHealthyAsync(
        BrainOwnerId ownerId,
        IReadOnlyCollection<CapabilityConnectionBinding> connections,
        CancellationToken cancellationToken = default);
}

internal sealed class OwnerConnectionHealth(IServiceProvider services) : IOwnerConnectionHealth
{
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlySet<CapabilityConnectionBinding>> ReadHealthyAsync(
        BrainOwnerId ownerId,
        IReadOnlyCollection<CapabilityConnectionBinding> connections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var requested = connections
            .Where(static connection => connection is not null && !string.IsNullOrWhiteSpace(connection.Provider))
            .Distinct()
            .OrderBy(static connection => connection.Provider, StringComparer.Ordinal)
            .ThenBy(static connection => connection.ConnectionId?.Value, StringComparer.Ordinal)
            .ToArray();
        var providers = requested
            .Select(static connection => connection.Provider)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var results = await Task.WhenAll(providers.Select(provider =>
            ProbeAsync(ownerId, provider, cancellationToken))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var healthyProviders = results
            .Where(static provider => provider is not null)
            .Select(static provider => provider!)
            .ToHashSet(StringComparer.Ordinal);
        return requested
            .Where(connection => healthyProviders.Contains(connection.Provider))
            .Where(static connection => connection.ConnectionId is null ||
                string.Equals(connection.ConnectionId.Value.Value, connection.Provider, StringComparison.Ordinal))
            .ToHashSet();
    }

    private async Task<string?> ProbeAsync(
        BrainOwnerId ownerId,
        string provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var connector = services.GetKeyedService<IConnector>(provider);
            if (connector is null || !string.Equals(connector.Descriptor.Id, provider, StringComparison.Ordinal))
                return null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ProbeDeadline);
            var health = await connector.TestConnectionAsync(
                new NeuronId(ownerId.Value),
                deadline.Token).ConfigureAwait(false);
            return health.Healthy ? provider : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
