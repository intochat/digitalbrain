using System.Diagnostics;
using System.Globalization;
using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class HostedRestart
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DeliveryLimit = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ADurableTurnAndDeliverySurviveAKernelRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        int[] kernelsBefore;

        await using (var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken))
        {
            await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
            await Healthy(app, cancellationToken);

            kernelsBefore = Kernels();

            using (var kernel = app.CreateHttpClient("probe"))
            {
                using var turn = await kernel.PostAsync(new Uri("/probe/turn", UriKind.Relative), content: null, cancellationToken);

                Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
                Assert.Equal(1, await CountAsync(kernel, "/probe/fired", cancellationToken));
                Assert.Equal(1, await SettledAsync(kernel, "/probe/delivered/Recorder", 1, cancellationToken));
            }

            await app.ResourceCommands.ExecuteCommandAsync("probe", "resource-restart", cancellationToken);
            await Healthy(app, cancellationToken);

            using var recovered = app.CreateHttpClient("probe");

            Assert.Equal(1, await CountAsync(recovered, "/probe/fired", cancellationToken));
            Assert.Equal(1, await CountAsync(recovered, "/probe/delivered/Recorder", cancellationToken));

            using var again = await recovered.PostAsync(new Uri("/probe/turn", UriKind.Relative), content: null, cancellationToken);

            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal(2, await SettledAsync(recovered, "/probe/delivered/Recorder", 2, cancellationToken));
        }

        Assert.Empty(Kernels().Except(kernelsBefore));
    }

    [Fact]
    public async Task AGroupChatResumesBetweenSuperstepsWithoutReplayingTheCompletedModel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await Healthy(app, cancellationToken);

        using (var probe = app.CreateHttpClient("probe"))
        {
            await PostOkAsync(probe, "/probe/ai/pause-continuation", cancellationToken);
            await PostOkAsync(probe, "/probe/ai/start", cancellationToken);

            Assert.Equal(1, await SettledAsync(probe, "/probe/ai/continuation-paused", 1, cancellationToken));
            Assert.Equal(2, await CountAsync(probe, "/probe/ai/revision", cancellationToken));
            Assert.Equal(2, await CountAsync(probe, "/probe/ai/advances", cancellationToken));
            Assert.Equal(1, await CountAsync(probe, "/probe/ai/model-entries", cancellationToken));
            Assert.Equal("Running", await TextAsync(probe, "/probe/ai/state", cancellationToken));
        }

        await app.ResourceCommands.ExecuteCommandAsync("probe", "resource-restart", cancellationToken);
        await Healthy(app, cancellationToken);

        using var recovered = app.CreateHttpClient("probe");

        Assert.Equal(
            "Succeeded",
            await SettledTextAsync(recovered, "/probe/ai/state", "Succeeded", cancellationToken));
        Assert.Equal(2, await CountAsync(recovered, "/probe/ai/revision", cancellationToken));
        Assert.Equal(2, await CountAsync(recovered, "/probe/ai/advances", cancellationToken));
        Assert.Equal(1, await CountAsync(recovered, "/probe/ai/model-entries", cancellationToken));
        Assert.Equal("hosted restart answer", await TextAsync(recovered, "/probe/ai/answer", cancellationToken));
    }

    [Fact]
    public async Task TheOrleansDashboardIsServedInDevelopment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await Healthy(app, cancellationToken);

        using var kernel = app.CreateHttpClient("probe");
        using var dashboard = await kernel.GetAsync(new Uri("/dashboard", UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    private static Task<global::Aspire.Hosting.ApplicationModel.ResourceEvent> Healthy(DistributedApplication app, CancellationToken cancellationToken)
        => app.ResourceNotifications.WaitForResourceHealthyAsync("probe", cancellationToken).WaitAsync(StartupLimit, cancellationToken);

    private static int[] Kernels() => Process.GetProcessesByName("DigitalBrain.ProbeHost").Select(process => process.Id).ToArray();

    private static async Task<int> CountAsync(HttpClient kernel, string path, CancellationToken cancellationToken)
    {
        using var response = await kernel.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        response.EnsureSuccessStatusCode();

        return int.Parse(await response.Content.ReadAsStringAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<string> TextAsync(
        HttpClient kernel,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await kernel.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task PostOkAsync(
        HttpClient kernel,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await kernel.PostAsync(
            new Uri(path, UriKind.Relative),
            content: null,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static async Task<string> SettledTextAsync(
        HttpClient kernel,
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + DeliveryLimit;
        var seen = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            seen = await TextAsync(kernel, path, cancellationToken);

            if (string.Equals(seen, expected, StringComparison.Ordinal))
            {
                return seen;
            }
        }

        return seen;
    }
}
