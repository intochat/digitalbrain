using System.Net;
using DigitalBrain.Aspire;
using DigitalBrain.Testing;
using DigitalBrain.Ui;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class UiHostComposition(UiFixture fixture)
{
    [Fact(DisplayName = "product client owner defaults to DefaultOwner when DigitalBrain:Owner is absent")]
    public void ResolveOwnerDefaultsToDev()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(
            DigitalBrainClientHostingExtensions.DefaultOwner,
            DigitalBrainClientHostingExtensions.ResolveOwner(configuration));
        Assert.Equal(UiFixture.DefaultOwner, DigitalBrainClientHostingExtensions.DefaultOwner);
    }

    [Fact(DisplayName = "product client owner reads DigitalBrain:Owner configuration key")]
    public void ResolveOwnerReadsConfiguredOwner()
    {
        const string configuredOwner = "edge-owner";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [UiFixture.OwnerConfigurationKey] = configuredOwner,
            })
            .Build();

        Assert.Equal(
            configuredOwner,
            DigitalBrainClientHostingExtensions.ResolveOwner(configuration));
    }

    [Fact(DisplayName = "MapUiHost exposes health and product shell routes on one composition path")]
    public async Task MapUiHostExposesHealthAndShellRoutes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<Orleans.IGrainFactory>(test.Cluster.Client);

        await using var app = builder.Build();
        app.MapUiHost();
        await app.StartAsync(cancellationToken);

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var health = await http.GetAsync(
            new Uri(UiFixture.HealthPath, UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(
            $"\"{UiEdgeContract.HealthResponse}\"",
            (await health.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var open = await http.PostAsJsonAsync(
            UiEdgeSse.OpenScene(UiFixture.DefaultShellName),
            new OpenSceneRequest("home", "Home"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, open.StatusCode);

        using var badCursor = await http.GetAsync(
            new Uri(UiEdgeSse.ShellEvents(UiFixture.DefaultShellName, afterSequence: -1), UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
    }
}
