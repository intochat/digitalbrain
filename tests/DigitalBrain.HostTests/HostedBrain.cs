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

        await hosted.WaitHealthyAsync("silo", cancellationToken);

        using var silo = hosted.CreateHttpClient("silo");
        using var health = await silo.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
