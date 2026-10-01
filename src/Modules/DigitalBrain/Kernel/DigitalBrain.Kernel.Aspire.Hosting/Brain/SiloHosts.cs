using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

// A silo host is whatever Aspire marked when the resource referenced the Orleans service
// rather than its client. Several may run; they share one cluster.
public static class SiloHosts
{
    public const string HttpEndpointName = "http";
    public const string SiloEndpointName = "orleans-silo";

    public static IReadOnlyList<IResource> Find(IEnumerable<IResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return resources.Where(IsSiloHost).ToArray();
    }

    public static bool IsSiloHost(IResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var endpoints = resource.Annotations.OfType<EndpointAnnotation>();
        return endpoints.Any(endpoint => endpoint.Name == SiloEndpointName)
            && endpoints.Any(endpoint => endpoint.Name == HttpEndpointName);
    }
}
