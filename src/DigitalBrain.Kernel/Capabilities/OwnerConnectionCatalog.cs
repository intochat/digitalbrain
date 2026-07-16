using DigitalBrain.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel.Capabilities;

public interface IOwnerConnectionCatalog
{
    Task<OwnerConnectionSnapshot[]> ReadAsync(
        BrainOwnerId ownerId,
        CancellationToken cancellationToken = default);
}

internal sealed class OwnerConnectionCatalog(IServiceProvider services, ICapabilityCatalog catalog) : IOwnerConnectionCatalog
{
    private static readonly TimeSpan ProbeDeadline = TimeSpan.FromSeconds(3);
    private static readonly string[] KnownProviders = ["google", "salesforce", "web"];

    public async Task<OwnerConnectionSnapshot[]> ReadAsync(
        BrainOwnerId ownerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unlockedByProvider = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var descriptor in catalog.Snapshot() ?? Array.Empty<CapabilityDescriptor>())
        {
            if (descriptor?.RequiredConnections is null)
                continue;
            foreach (var provider in descriptor.RequiredConnections)
            {
                if (string.IsNullOrWhiteSpace(provider))
                    continue;
                if (!unlockedByProvider.TryGetValue(provider, out var ids))
                {
                    ids = new SortedSet<string>(StringComparer.Ordinal);
                    unlockedByProvider[provider] = ids;
                }
                ids.Add(descriptor.Id);
            }
        }

        foreach (var provider in KnownProviders)
        {
            if (!unlockedByProvider.ContainsKey(provider))
                unlockedByProvider[provider] = new SortedSet<string>(StringComparer.Ordinal);
        }

        var orderedProviders = unlockedByProvider.Keys
            .OrderBy(static provider => provider, StringComparer.Ordinal)
            .ToArray();
        var snapshots = new OwnerConnectionSnapshot[orderedProviders.Length];
        for (var index = 0; index < orderedProviders.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = orderedProviders[index];
            snapshots[index] = await ProjectAsync(
                    ownerId,
                    provider,
                    unlockedByProvider[provider].ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return snapshots;
    }

    private async Task<OwnerConnectionSnapshot> ProjectAsync(
        BrainOwnerId ownerId,
        string provider,
        string[] unlockedCapabilityIds,
        CancellationToken cancellationToken)
    {
        var connectPath = $"/oauth/start/{provider}";
        var connector = services.GetKeyedService<IConnector>(provider);
        if (connector is null || !string.Equals(connector.Descriptor.Id, provider, StringComparison.Ordinal))
        {
            return new OwnerConnectionSnapshot(
                provider,
                provider,
                provider,
                OwnerConnectionHealthStatus.Disconnected,
                "Connector is not registered.",
                unlockedCapabilityIds,
                connectPath);
        }

        var displayName = string.IsNullOrWhiteSpace(connector.Descriptor.DisplayName)
            ? provider
            : connector.Descriptor.DisplayName;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(ProbeDeadline);
            var config = await connector.ValidateConfigAsync(
                    IntegrationConfigScopes.ForUser(new UserId(ownerId.Value)),
                    deadline.Token)
                .ConfigureAwait(false);
            if (!config.IsValid)
            {
                return new OwnerConnectionSnapshot(
                    provider,
                    provider,
                    displayName,
                    OwnerConnectionHealthStatus.Misconfigured,
                    config.Message ?? config.MissingKey,
                    unlockedCapabilityIds,
                    connectPath);
            }

            var health = await connector.TestConnectionAsync(
                    new NeuronId(ownerId.Value),
                    deadline.Token)
                .ConfigureAwait(false);
            if (health.Healthy)
            {
                return new OwnerConnectionSnapshot(
                    provider,
                    provider,
                    displayName,
                    OwnerConnectionHealthStatus.Healthy,
                    health.Detail,
                    unlockedCapabilityIds,
                    connectPath);
            }

            return new OwnerConnectionSnapshot(
                provider,
                provider,
                displayName,
                OwnerConnectionHealthStatus.NeedsReauth,
                health.Detail,
                unlockedCapabilityIds,
                connectPath);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OwnerConnectionSnapshot(
                provider,
                provider,
                displayName,
                OwnerConnectionHealthStatus.Disconnected,
                "Connection probe timed out.",
                unlockedCapabilityIds,
                connectPath);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new OwnerConnectionSnapshot(
                provider,
                provider,
                displayName,
                OwnerConnectionHealthStatus.Disconnected,
                "Connection probe failed.",
                unlockedCapabilityIds,
                connectPath);
        }
    }
}
