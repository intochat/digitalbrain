using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[Collection(HostedApplication.CollectionName)]
public sealed class HostedBrain(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.App.WaitHttpReadyAsync("silo", cancellationToken: cancellationToken);

        using var silo = fixture.App.CreateHttpClient("silo");
        using var health = await silo.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
