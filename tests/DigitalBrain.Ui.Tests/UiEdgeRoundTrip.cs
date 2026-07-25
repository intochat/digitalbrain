using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using DigitalBrain.Abstractions;
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

        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        using var openResponse = await http.PostAsJsonAsync(
            "/shells/desk/scenes",
            new OpenSceneRequest("home", "Home"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", journaled.Synapse.SceneKey);

        var projected = await ReadNextSceneOpenedEventAsync(events.Reader, cancellationToken);

        Assert.Equal("home", projected.SceneKey);
        Assert.Equal("Home", projected.Title);
        Assert.Equal(journaled.Sequence, projected.Sequence);
    }

    [Fact(DisplayName = "IDigitalBrain mutator journals SceneOpened and SSE projects without restart")]
    public async Task DigitalBrainMutatorJournalsAndSseProjectsWithoutRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");
        var command = new OpenScene(CommandId.New(), "settings", "Settings");

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);
        http.Timeout = TimeSpan.FromSeconds(30);

        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        await test.Client.Get<IShell>("desk").Open(command);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, journaled.Synapse.CommandId);
        Assert.Equal("settings", journaled.Synapse.SceneKey);
        Assert.Equal("Settings", journaled.Synapse.Title);
        Assert.Equal(shell.Id, journaled.Synapse.Shell);

        var projected = await ReadNextSceneOpenedEventAsync(events.Reader, cancellationToken);

        Assert.Equal(journaled.Sequence, projected.Sequence);
        Assert.Equal("settings", projected.SceneKey);
        Assert.Equal("Settings", projected.Title);
        Assert.Equal(command.CommandId.ToString(), projected.CommandId);
        Assert.Equal(shell.Id.ToString(), projected.Shell);
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

    [Fact(DisplayName = "Ui shell SSE projection is journal-session backed not OpenTelemetry")]
    public void ShellSseProjectionIsJournalSessionNotOpenTelemetry()
    {
        var write = typeof(ShellEventFeed).GetMethod(
            nameof(ShellEventFeed.WriteSceneOpenedSseAsync),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(write);

        var parameters = write.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        Assert.Contains(typeof(IGrainFactory), parameters);
        Assert.Contains(typeof(IDigitalBrain), parameters);
        Assert.Contains(typeof(long), parameters);
        Assert.DoesNotContain(
            parameters,
            type => (type.FullName ?? type.Name).Contains("OpenTelemetry", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(System.Diagnostics.Activity), parameters);
        Assert.DoesNotContain(typeof(System.Diagnostics.ActivitySource), parameters);

        foreach (var method in typeof(ShellEventFeed).GetMethods(
                     BindingFlags.Public
                     | BindingFlags.NonPublic
                     | BindingFlags.Static
                     | BindingFlags.Instance
                     | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in method.GetParameters())
            {
                var typeName = parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                Assert.False(
                    typeName.Contains("OpenTelemetry", StringComparison.Ordinal),
                    $"{method.Name} must not take OpenTelemetry product types; took {typeName}.");
            }
        }
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

    private static async Task<ShellEventStream> OpenShellEventStreamAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/shells/desk/events?afterSequence=0");
        var streamResponse = await http.SendAsync(
            streamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (streamResponse.StatusCode != HttpStatusCode.OK)
        {
            var errorBody = await streamResponse.Content.ReadAsStringAsync(cancellationToken);
            streamResponse.Dispose();
            Assert.Fail(
                $"SSE status {(int)streamResponse.StatusCode} {streamResponse.StatusCode}: {errorBody}");
        }

        Assert.Equal("text/event-stream", streamResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            "no-cache",
            streamResponse.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        return new ShellEventStream(streamResponse, body);
    }

    private static async Task<SceneOpenedEvent> ReadNextSceneOpenedEventAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        string? dataLine = null;
        string? eventName = null;
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLine = line["data:".Length..].Trim();
                continue;
            }

            if (line.Length == 0 && dataLine is not null)
            {
                var name = eventName;
                var payload = dataLine;
                eventName = null;
                dataLine = null;

                if (name is not null
                    && !string.Equals(name, "scene-opened", StringComparison.Ordinal))
                {
                    continue;
                }

                var projected = System.Text.Json.JsonSerializer.Deserialize<SceneOpenedEvent>(
                    payload,
                    EventJsonOptions);
                if (projected is not null
                    && !string.IsNullOrWhiteSpace(projected.SceneKey)
                    && projected.Sequence > 0)
                {
                    return projected;
                }
            }
        }

        throw new TimeoutException(
            "SSE stream ended before a SceneOpened projection arrived — brain may have moved while UI projection is dead.");
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        var address = app.Urls.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private sealed class ShellEventStream : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _body;

        public ShellEventStream(HttpResponseMessage response, Stream body)
        {
            _response = response;
            _body = body;
            Reader = new StreamReader(body);
        }

        public StreamReader Reader { get; }

        public async ValueTask DisposeAsync()
        {
            Reader.Dispose();
            await _body.DisposeAsync();
            _response.Dispose();
        }
    }
}
