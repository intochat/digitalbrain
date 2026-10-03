namespace DigitalBrain.Registry;

[GenerateSerializer, Alias("registry.discovery")]
public sealed record RegistryDiscovery(
    [property: Id(0)] RegistryCapability[] Capabilities,
    [property: Id(1)] RegistryDiscoveryError[] Errors);

[GenerateSerializer, Alias("registry.capability")]
public sealed record RegistryCapability(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] string[] Tools,
    [property: Id(3)] string? ResourcesJson = null);

[GenerateSerializer, Alias("registry.discovery-error")]
public sealed record RegistryDiscoveryError(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Message);

// Host DI extension; scope always comes from the kernel-stamped ambient caller.
public interface IRegistryResourceProvider
{
    string Id { get; }
    Task<RegistryDiscovery> Discover(CancellationToken cancellationToken);
}
