extern alias McpProject;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using FeatureCapabilityCatalogClient = McpProject::DigitalBrain.Mcp.FeatureCapabilityCatalogClient;
using FeatureCapabilityCatalogServiceCollectionExtensions = McpProject::DigitalBrain.Mcp.FeatureCapabilityCatalogServiceCollectionExtensions;
using IFeatureCapabilityCatalog = McpProject::DigitalBrain.Mcp.IFeatureCapabilityCatalog;

namespace DigitalBrain.Tests.Runtime;

public sealed class FeatureCapabilityCatalogClientTests
{
    [Fact]
    public void Production_registration_resolves_the_Orleans_authority_catalog_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton(DispatchProxy.Create<IClusterClient, ThrowingProxy>());

        FeatureCapabilityCatalogServiceCollectionExtensions.AddFeatureCapabilityCatalog(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<FeatureCapabilityCatalogClient>(
            provider.GetRequiredService<IFeatureCapabilityCatalog>());
    }

    public class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException();
    }
}
