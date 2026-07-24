using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedBrain(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource("silo");

        await silo.WaitUntilHealthyAsync(cancellationToken);

        using var client = silo.CreateHttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
