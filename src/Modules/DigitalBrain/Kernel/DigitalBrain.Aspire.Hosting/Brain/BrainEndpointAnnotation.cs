using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed record BrainBrowserAnnotation(string Endpoint, string Path = "/", string? ReadySelector = null) : IResourceAnnotation;

public static class BrainEndpointExtensions
{
    public static IResourceBuilder<T> AsBrainBrowser<T>(this IResourceBuilder<T> resource, string endpoint = "http",
        string path = "/", string? readySelector = null) where T : IResourceWithEndpoints
        => resource.WithAnnotation(new BrainBrowserAnnotation(endpoint, path, readySelector));
}