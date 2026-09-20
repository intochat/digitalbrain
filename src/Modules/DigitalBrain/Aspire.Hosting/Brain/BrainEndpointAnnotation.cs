using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed record BrainEndpointAnnotation(string Endpoint) : IResourceAnnotation;
public sealed record BrainBrowserAnnotation(string Endpoint) : IResourceAnnotation;

public static class BrainEndpointExtensions
{
    public static IResourceBuilder<T> AsPrimaryBrain<T>(this IResourceBuilder<T> resource, string endpoint = "http") where T : IResourceWithEndpoints
        => resource.WithAnnotation(new BrainEndpointAnnotation(endpoint));
    public static IResourceBuilder<T> AsBrainBrowser<T>(this IResourceBuilder<T> resource, string endpoint = "http") where T : IResourceWithEndpoints
        => resource.WithAnnotation(new BrainBrowserAnnotation(endpoint));
}
