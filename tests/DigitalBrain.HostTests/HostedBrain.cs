using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[Collection(HostedApplication.CollectionName)]
public sealed class HostedBrain
{
    [Fact]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hosted = await HostedApplication.OpenAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken: cancellationToken);

        await hosted.WaitHttpReadyAsync("silo", cancellationToken: cancellationToken);

        using var silo = hosted.CreateHttpClient("silo");
        silo.Timeout = TimeSpan.FromSeconds(30);
        using var health = await silo.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
