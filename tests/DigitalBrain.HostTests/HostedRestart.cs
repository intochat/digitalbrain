using System.Globalization;
using System.Net;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[Collection(HostedApplication.CollectionName)]
public sealed class HostedRestart
{
    private static readonly TimeSpan DeliveryLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ADurableTurnAndDeliverySurviveAKernelRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hosted = await HostedApplication.OpenAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken: cancellationToken);

        await hosted.WaitHealthyAsync("probe", cancellationToken);

        using (var kernel = hosted.CreateHttpClient("probe"))
        {
            using var turn = await kernel.PostAsync(new Uri("/probe/turn", UriKind.Relative), content: null, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
            Assert.Equal(1, await CountAsync(kernel, "/probe/fired", cancellationToken));
            Assert.Equal(1, await SettledAsync(kernel, "/probe/delivered/Recorder", 1, cancellationToken));
        }

        await hosted.RestartResourceAsync("probe", cancellationToken);
        await hosted.WaitHealthyAsync("probe", cancellationToken);

        using var recovered = hosted.CreateHttpClient("probe");

        Assert.Equal(1, await CountAsync(recovered, "/probe/fired", cancellationToken));
        Assert.Equal(1, await CountAsync(recovered, "/probe/delivered/Recorder", cancellationToken));

        using var again = await recovered.PostAsync(new Uri("/probe/turn", UriKind.Relative), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, await SettledAsync(recovered, "/probe/delivered/Recorder", 2, cancellationToken));
    }

    [Fact]
    public async Task TheOrleansDashboardIsServedInDevelopment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var hosted = await HostedApplication.OpenAsync<Projects.DigitalBrain_TestingAppHost>(
            cancellationToken: cancellationToken);

        await hosted.WaitHealthyAsync("probe", cancellationToken);

        using var kernel = hosted.CreateHttpClient("probe");
        using var dashboard = await kernel.GetAsync(new Uri("/dashboard", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    private static async Task<int> CountAsync(HttpClient kernel, string path, CancellationToken cancellationToken)
    {
        using var response = await kernel.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        response.EnsureSuccessStatusCode();

        return int.Parse(await response.Content.ReadAsStringAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> SettledAsync(HttpClient kernel, string path, int expected, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + DeliveryLimit;
        var seen = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            seen = await CountAsync(kernel, path, cancellationToken);

            if (seen >= expected)
            {
                return seen;
            }
        }

        return seen;
    }
}
