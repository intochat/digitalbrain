using System.Net;
using System.Net.Http.Json;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Testing;
using DigitalBrain.Ui;
using Orleans;
using Xunit;

namespace DigitalBrain.Ui.Tests;

public sealed class UiEdgeRoundTrip(UiFixture fixture)
{
    private static readonly System.Text.Json.JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName = "HTTP open-scene reaches IDigitalBrain and journals SceneOpened")]
    public async Task HttpOpenSceneJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
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

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
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

    [Fact(DisplayName = "SSE shell events projects SceneOpened after open without process restart")]
    public async Task HttpShellEventsProjectsSceneOpenedWithoutRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);
        http.Timeout = TimeSpan.FromSeconds(30);

        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/shells/desk/events?afterSequence=0");
        using var streamResponse = await http.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (streamResponse.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await streamResponse.Content.ReadAsStringAsync(cancellationToken);
            Assert.Fail(
                $"SSE status {(int)streamResponse.StatusCode} {streamResponse.StatusCode}: {errorBody}");
        }

        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "no-cache",
            streamResponse.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        await using var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body);

        using var openResponse = await http.PostAsJsonAsync(
            "/shells/desk/scenes",
            new OpenSceneRequest("home", "Home"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", journaled.Synapse.SceneKey);

        var projected = await ReadNextSceneOpenedEventAsync(reader, cancellationToken);

        Assert.Equal("home", projected.SceneKey);
        Assert.Equal("Home", projected.Title);
        Assert.True(projected.Sequence > 0);
    }

    [Fact(DisplayName = "SSE shell events rejects negative afterSequence")]
    public async Task HttpShellEventsRejectsNegativeAfterSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.GetAsync(
            new Uri("/shells/desk/events?afterSequence=-1", UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<WebApplication> StartUiEdgeAsync(
        TestBrain test,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<IGrainFactory>(test.Cluster.Client);

        var app = builder.Build();
        app.MapUi();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static async Task<SceneOpenedEvent> ReadNextSceneOpenedEventAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        string? dataLine = null;
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLine = line["data:".Length..].Trim();
            }
            else if (line.Length == 0 && dataLine is not null)
            {
                var projected = System.Text.Json.JsonSerializer.Deserialize<SceneOpenedEvent>(
                    dataLine,
                    EventJsonOptions);
                if (projected is not null
                    && !string.IsNullOrWhiteSpace(projected.SceneKey))
                {
                    return projected;
                }

                dataLine = null;
            }
        }

        throw new TimeoutException("SSE stream ended before a SceneOpened projection arrived.");
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Urls.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }
}
