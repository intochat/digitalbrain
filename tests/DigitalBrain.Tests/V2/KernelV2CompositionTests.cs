using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Hosting;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Tests.V2;

public sealed class KernelV2CompositionTests
{
    [Fact]
    public async Task Actual_v2_kernel_graph_has_no_legacy_gateway_bus_or_stream_provider()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(DigitalBrainOrleansExtensions).Assembly.GetName().Name
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime"] = "V2",
            ["DigitalBrain:TestMode"] = "true"
        });
        builder.AddServiceDefaults();
        builder.UseDigitalBrainOrleans();
        builder.AddDigitalBrainClients();

        var descriptors = builder.Services.ToArray();
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(GatewayService));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(UiGatewayService));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(HomeFeedBus));
        Assert.DoesNotContain(descriptors, descriptor => descriptor.ServiceType == typeof(SignalEgressBus));
        Assert.DoesNotContain(descriptors, descriptor =>
            string.Equals(descriptor.ServiceType.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal) ||
            string.Equals(descriptor.ImplementationType?.FullName, "DigitalBrain.Kernel.Ui.SignalEgressStreamSubscriber", StringComparison.Ordinal));

        var graph = string.Join('\n', descriptors.Select(static descriptor =>
            $"{descriptor.ServiceType.FullName}|{descriptor.ServiceKey}|{descriptor.ImplementationType?.FullName}"));
        Assert.DoesNotContain("HomeFeed", graph, StringComparison.Ordinal);
        Assert.DoesNotContain(SynapseStream.ProviderName, graph, StringComparison.Ordinal);
        Assert.DoesNotContain("PubSubStore", graph, StringComparison.Ordinal);

        await using var app = builder.Build();
        app.MapDigitalBrainSetup();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).ToArray();
        var endpointGraph = string.Join('\n', endpoints.Select(static endpoint =>
            endpoint is RouteEndpoint route ? $"{endpoint.DisplayName}|{route.RoutePattern.RawText}" : endpoint.DisplayName));
        Assert.DoesNotContain("digitalbrain.DigitalBrainGateway", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("digitalbrain.ui.UiGateway", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WatchHomeFeed", endpointGraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WatchSynapses", endpointGraph, StringComparison.OrdinalIgnoreCase);
    }
}
