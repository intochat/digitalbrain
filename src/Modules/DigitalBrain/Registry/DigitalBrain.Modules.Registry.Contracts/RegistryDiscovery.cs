namespace DigitalBrain.Registry;

[GenerateSerializer, Alias("registry.discovery")]
public sealed record RegistryDiscovery(
    [property: Id(0)] RegistryCapability[] Capabilities,
    [property: Id(1)] RegistryDiscoveryError[] Errors,
    [property: Id(2)] int? NextOffset = null);

[GenerateSerializer, Alias("registry.capability")]
public sealed record RegistryCapability(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] string[] Tools,
    [property: Id(3)] string? ResourcesJson = null,
    [property: Id(4)] string? ToolSource = null,
    [property: Id(5)] string? ToolResource = null);

[GenerateSerializer, Alias("registry.discovery-error")]
public sealed record RegistryDiscoveryError(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Message);

// Host DI extension; scope always comes from the kernel-stamped ambient caller.
public interface IRegistryResourceProvider
{
    string Id { get; }
    RegistryCapability Summary => new(Id, Id, []);
    Task<RegistryDiscovery> Discover(CancellationToken cancellationToken);
    async Task<RegistryDiscovery> Browse(int offset, int limit, CancellationToken cancellationToken)
    {
        var result = await Discover(cancellationToken);
        return result with { Capabilities = result.Capabilities.Skip(offset).Take(limit).ToArray(),
            NextOffset = result.Capabilities.Length > offset + limit ? offset + limit : null };
    }
    async Task<RegistryCapability?> Select(string id, CancellationToken cancellationToken)
        => (await Discover(cancellationToken)).Capabilities.SingleOrDefault(item => item.Id == id);
}
