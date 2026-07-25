using System.Net;
using DigitalBrain.Aspire;
using DigitalBrain.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class UiHostComposition(UiFixture fixture)
{
    [Fact(DisplayName = "product client owner defaults to dev when DigitalBrain:Owner is absent")]
    public void ResolveOwnerDefaultsToDev()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(
            DigitalBrainClientHostingExtensions.DefaultOwner,
            DigitalBrainClientHostingExtensions.ResolveOwner(configuration));
        Assert.Equal("dev", DigitalBrainClientHostingExtensions.DefaultOwner);
    }

    [Fact(DisplayName = "product client owner reads DigitalBrain:Owner configuration key")]
    public void ResolveOwnerReadsConfiguredOwner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DigitalBrainClientHostingExtensions.OwnerConfigurationKey] = "edge-owner",
            })
            .Build();

        Assert.Equal(
            "edge-owner",
            DigitalBrainClientHostingExtensions.ResolveOwner(configuration));
    }

    [Fact(DisplayName = "MapUiHost exposes /health and product shell routes on one composition path")]
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

        using var health = await http.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("\"healthy\"", (await health.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var open = await http.PostAsJsonAsync(
            "/shells/desk/scenes",
            new OpenSceneRequest("home", "Home"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, open.StatusCode);

        using var badCursor = await http.GetAsync(
            new Uri("/shells/desk/events?afterSequence=-1", UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
    }
}
