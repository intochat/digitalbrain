using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Aspire.Hosting.Tests.Unit;

public sealed class DeploymentIdentityFacts
{
    [Fact]
    public async Task LocalDevelopmentSiloBindsTheAdvertisedLocalhostGateway()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var server = builder.AddExecutable("server", "unused", ".").WithReference(builder.AddDigitalBrain("memory"));
        var configuration = await ExecutionConfigurationBuilder.Create(server.Resource).WithEnvironmentVariablesConfig()
            .AddExecutionConfigurationGatherer(new ServiceKeys())
            .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.Equal("127.0.0.1", configuration.EnvironmentVariables.ToDictionary()["Orleans__Endpoints__AdvertisedIPAddress"]);
        Assert.All(server.Resource.Annotations.OfType<EndpointAnnotation>(), endpoint => Assert.False(endpoint.IsProxied));
    }

    [Fact]
    public async Task ScopedResourcesProjectStableProviderServiceKeys()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var brain = builder.AddDigitalBrain("renamed", options: new() { UseAzureStorage = true });
        var server = builder.AddExecutable("server", "unused", ".").WithReference(brain);
        var client = builder.AddExecutable("client", "unused", ".").WithReference(brain.AsClient());
        foreach (var resource in new[] { server.Resource, client.Resource })
        {
            var configuration = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
                .AddExecutionConfigurationGatherer(new ServiceKeys())
                .BuildAsync(builder.ExecutionContext, NullLogger.Instance, TestContext.Current.CancellationToken);
            Assert.Equal(DigitalBrainNames.Clustering, configuration.EnvironmentVariables.ToDictionary()["Orleans__Clustering__ServiceKey"]);
            if (resource == server.Resource)
            {
                Assert.Equal(DigitalBrainNames.Reminders, configuration.EnvironmentVariables.ToDictionary()["Orleans__Reminders__ServiceKey"]);
                Assert.Equal(DigitalBrainNames.GrainState, configuration.EnvironmentVariables.ToDictionary()["Orleans__GrainStorage__Default__ServiceKey"]);
            }
        }
    }

    private sealed class ServiceKeys : IExecutionConfigurationGatherer
    {
        public ValueTask GatherAsync(IExecutionConfigurationGathererContext context, IResource resource,
            ILogger resourceLogger, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken = default)
        {
            foreach (var key in context.EnvironmentVariables.Keys.Where(k => !k.EndsWith("__ServiceKey", StringComparison.Ordinal)
                && k != "Orleans__Endpoints__AdvertisedIPAddress").ToArray())
            { context.EnvironmentVariables.Remove(key); }
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void ClusterChangesDoNotMovePersistentState()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        builder.Configuration["Orleans:ClusterId"] = "new-run";
        var brain = builder.AddDigitalBrain("renamed-resource", serviceId: "existing-data");
        Assert.Equal("existing-data", brain.ServiceId);
        Assert.Equal("new-run", brain.ClusterId);
        Assert.Equal("default-service", builder.AddDigitalBrain("default-service").ServiceId);
    }

    [Fact]
    public void MultipleServersRequireExplicitSelectionAndResolveTheirOwnBrain()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = [], DisableDashboard = true });
        var first = builder.AddDigitalBrain("first", serviceId: "first-data", options: new() { UseAzureStorage = true, ClusterId = "first-run" });
        var second = builder.AddDigitalBrain("second", serviceId: "second-data", options: new() { UseAzureStorage = true, ClusterId = "second-run" });
        builder.AddExecutable("first-server", "dotnet", ".").WithReference(first);
        builder.AddExecutable("second-server", "dotnet", ".").WithReference(second);
        Assert.Throws<InvalidOperationException>(() => SiloHosts.Select(builder.Resources));
        var selected = SiloHosts.BrainOf(SiloHosts.Select(builder.Resources, "second-server"));
        Assert.Same(second, selected);
        Assert.Equal("second-data", selected.ServiceId);
        Assert.Equal("second-run", selected.ClusterId);
        Assert.Equal(second.ResourceName(DigitalBrain.Contracts.DigitalBrainNames.Clustering), selected.ClusteringResourceName);
    }
}
