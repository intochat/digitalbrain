using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedBrain
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        await app.ResourceNotifications
            .WaitForResourceHealthyAsync("silo", cancellationToken)
            .WaitAsync(StartupLimit, cancellationToken);

        using var silo = app.CreateHttpClient("silo");
        using var health = await silo.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
    }
}
