using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedRestart
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ADurableTurnSurvivesAKernelRestartOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("probe", cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        using (var kernel = app.CreateHttpClient("probe"))
        {
            using var turn = await kernel.PostAsync(new Uri("/probe/turn", UriKind.Relative), content: null, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
            Assert.Equal(1, await FiredAsync(kernel, cancellationToken));
        }

        await app.ResourceCommands.ExecuteCommandAsync("probe", "resource-restart", cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("probe", cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        using var recovered = app.CreateHttpClient("probe");

        Assert.Equal(1, await FiredAsync(recovered, cancellationToken));
    }

    private static async Task<int> FiredAsync(HttpClient kernel, CancellationToken cancellationToken)
    {
        using var fired = await kernel.GetAsync(new Uri("/probe/fired", UriKind.Relative), cancellationToken);

        fired.EnsureSuccessStatusCode();

        return int.Parse(await fired.Content.ReadAsStringAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
