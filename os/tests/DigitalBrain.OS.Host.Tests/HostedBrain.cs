using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedBrain(TestingAppHostFixture fixture)
{
    [Fact(
        Timeout = 300_000,
        DisplayName =
            "TestingAppHost silo-only residual: silo Healthy and health path OK (not product OS surface)")]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource(TestingAppHostFixture.SiloResourceName);

        await silo.WaitUntilHealthyAsync(cancellationToken);

        using var client = silo.CreateHttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        using var health = await client.GetAsync(
            new Uri(TestingAppHostFixture.HealthPath, UriKind.Relative),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
