using System.Globalization;
using System.Net;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class Topology(TestingAppHostFixture fixture)
{
    [Fact(DisplayName = "testing topology serves silo and probe through their own handles")]
    public async Task TestingTopologyServesSiloAndProbeThroughTheirOwnHandles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource("silo");
        var probe = host.Resource("probe");

        await silo.WaitUntilHealthyAsync(cancellationToken);
        await probe.WaitUntilHealthyAsync(cancellationToken);

        using var siloClient = silo.CreateHttpClient();
        using var siloHealth = await siloClient.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);
        using var probeClient = probe.CreateHttpClient();
        using var probeHealth = await probeClient.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);
        using var probeFired = await probeClient.GetAsync(
            new Uri("/probe/fired", UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, siloHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, probeHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, probeFired.StatusCode);
        Assert.True(
            int.TryParse(
                await probeFired.Content.ReadAsStringAsync(cancellationToken),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _));
    }
}
