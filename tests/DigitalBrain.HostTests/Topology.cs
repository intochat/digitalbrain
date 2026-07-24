using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[Collection(HostedApplication.CollectionName)]
public sealed class Topology
{
    [Fact(DisplayName = "different silo applications use different Orleans brains")]
    public async Task DifferentSiloApplicationsUseDifferentBrains()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var appHost = await HostedApplication.CreateBuilderAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken);
        var silo = Assert.IsAssignableFrom<IResourceWithEnvironment>(
            Assert.Single(appHost.Resources, resource => resource.Name == "silo"));
        var probe = Assert.IsAssignableFrom<IResourceWithEnvironment>(
            Assert.Single(appHost.Resources, resource => resource.Name == "probe"));
        var siloEnvironment = await ProjectAsync(silo);
        var probeEnvironment = await ProjectAsync(probe);

        Assert.Equal("brain-clustering", siloEnvironment["Orleans__Clustering__ServiceKey"]);
        Assert.Equal("brain-reminders", siloEnvironment["Orleans__Reminders__ServiceKey"]);
        Assert.Equal("probe-clustering", probeEnvironment["Orleans__Clustering__ServiceKey"]);
        Assert.Equal("probe-reminders", probeEnvironment["Orleans__Reminders__ServiceKey"]);
        Assert.NotEqual(siloEnvironment["Orleans__ClusterId"], probeEnvironment["Orleans__ClusterId"]);
        Assert.NotEqual(siloEnvironment["Orleans__ServiceId"], probeEnvironment["Orleans__ServiceId"]);
    }

    [Fact(DisplayName = "R-3: no resource publishes an endpoint outside the host")]
    public async Task NoResourcePublishesAnExternalEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var appHost = await HostedApplication.CreateBuilderAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken);

        var external = appHost.Resources
            .SelectMany(
                resource => resource.Annotations.OfType<EndpointAnnotation>(),
                (resource, endpoint) => (resource.Name, endpoint.Name, endpoint.IsExternal))
            .Where(candidate => candidate.IsExternal)
            .Select(candidate => $"{candidate.Item1}/{candidate.Item2}")
            .ToList();

        Assert.Empty(external);
    }

    [Fact(DisplayName = "R-3: the Orleans gateway a client connects to is never published outside the host")]
    public async Task TheClusteringEndpointsAreHostAllocatedAndInternal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var appHost = await HostedApplication.CreateBuilderAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken);

        var silo = Assert.Single(appHost.Resources, resource => resource.Name == "silo");
        var clustering = silo.Annotations
            .OfType<EndpointAnnotation>()
            .Where(endpoint => endpoint.Name.StartsWith("orleans-", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
            ["orleans-gateway", "orleans-silo"],
            clustering.Select(endpoint => endpoint.Name).Order(StringComparer.Ordinal));

        foreach (var endpoint in clustering)
        {
            Assert.False(endpoint.IsExternal, $"{endpoint.Name} is published outside the host");
            Assert.Null(endpoint.Port);
        }
    }

    private static async Task<Dictionary<string, object>> ProjectAsync(IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish));

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
    }
}
