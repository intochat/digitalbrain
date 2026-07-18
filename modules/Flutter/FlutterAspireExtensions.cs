using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Brain.Modules.Flutter;

public static class FlutterAspireExtensions
{
    public static IResourceBuilder<T> WithDigitalBrainFlutter<T>(
        this IResourceBuilder<T> kernel)
        where T : IResourceWithEnvironment, IResourceWithEndpoints =>
        kernel.WithHttpEndpoint(port: 5320, name: "flutter-gateway");
}
