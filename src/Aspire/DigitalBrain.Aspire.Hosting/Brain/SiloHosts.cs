using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

// A silo host is whatever Aspire marked when the resource referenced the Orleans service
// rather than its client. Several may run; they share one cluster.
internal sealed record BrainSiloAnnotation(DigitalBrainBuilder Brain) : IResourceAnnotation;

public static class SiloHosts
{
    public const string HttpEndpointName = "http";
    public const string SiloEndpointName = "orleans-silo";

    public static IReadOnlyList<IResource> Find(IEnumerable<IResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Where(IsSiloHost).ToArray();
    }

    public static DigitalBrainBuilder BrainOf(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var references = resource.Annotations.OfType<BrainSiloAnnotation>().ToArray();
        return references.Length == 1 ? references[0].Brain
            : throw new InvalidOperationException($"Resource '{resource.Name}' must reference exactly one brain.");
    }

    public static IResource Select(IEnumerable<IResource> resources, string? serverName = null)
    {
        var hosts = Find(resources).Where(r => serverName is null || r.Name == serverName).ToArray();
        return hosts.Length == 1 ? hosts[0]
            : throw new InvalidOperationException("Select exactly one brain server with ServerResourceName.");
    }

    public static bool IsSiloHost(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.Annotations.OfType<BrainSiloAnnotation>().Any();
    }
}
