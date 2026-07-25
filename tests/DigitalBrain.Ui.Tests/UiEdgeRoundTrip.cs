using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Testing;
using DigitalBrain.Ui;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class UiEdgeRoundTrip(UiFixture fixture)
{
    [Fact(DisplayName = "HTTP open-scene reaches IDigitalBrain and journals SceneOpened")]
    public async Task HttpOpenSceneJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await using var app = await StartUiEdgeAsync(test.Client, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            "/shells/desk/scenes",
            new OpenSceneRequest("home", "Home"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", opened.Synapse.SceneKey);
        Assert.Equal("Home", opened.Synapse.Title);
        Assert.Equal(shell.Id, opened.Synapse.Shell);
    }

    [Fact(DisplayName = "HTTP control activation journals ControlActivated on the scene")]
    public async Task HttpControlActivationJournalsControlActivated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>("home");

        await using var app = await StartUiEdgeAsync(test.Client, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            "/scenes/home/controls/primary/activate",
            new ActivateControlRequest("submit"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var activation = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal("home", activation.Synapse.SceneKey);
        Assert.Equal("primary", activation.Synapse.ControlId);
        Assert.Equal("submit", activation.Synapse.Intent);
    }

    private static async Task<WebApplication> StartUiEdgeAsync(
        IDigitalBrain brain,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(brain);

        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok("healthy"));
        app.MapUi();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Urls.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }
}
