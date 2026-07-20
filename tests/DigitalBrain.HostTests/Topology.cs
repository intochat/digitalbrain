using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class Topology
{
    [Fact(DisplayName = "R-3: no resource publishes an endpoint outside the host")]
    public async Task NoResourcePublishesAnExternalEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

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
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

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
}
