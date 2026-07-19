using Aspire.Hosting;
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

        var silo = await app.ResourceNotifications
            .WaitForResourceHealthyAsync("silo", cancellationToken)
            .WaitAsync(StartupLimit, cancellationToken);

        Assert.Equal("silo", silo.Resource.Name);
    }
}
