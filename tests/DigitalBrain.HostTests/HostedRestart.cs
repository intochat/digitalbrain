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
    public async Task ANeuronAnswersThroughTheScriptedModelOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_TestingAppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);

        await app.StartAsync(cancellationToken).WaitAsync(StartupLimit, cancellationToken);
        await Healthy(app, cancellationToken);

        using var kernel = app.CreateHttpClient("probe");
        using var asked = await kernel.PostAsync(new Uri("/probe/ask", UriKind.Relative), content: null, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);

        var deadline = DateTimeOffset.UtcNow + DeliveryLimit;

        while (DateTimeOffset.UtcNow < deadline)
        {
            using var answers = await kernel.GetAsync(new Uri("/probe/answers", UriKind.Relative), cancellationToken);
            var text = await answers.Content.ReadAsStringAsync(cancellationToken);

            if (text.Contains("the kernel is awake", StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail("the scripted model never answered on the real host");
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
