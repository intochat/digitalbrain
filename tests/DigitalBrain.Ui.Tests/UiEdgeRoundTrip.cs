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
    private const string HomeSceneKey = "home";
    private const string HomeTitle = "Home";
    private const string SettingsSceneKey = "settings";
    private const string SettingsTitle = "Settings";
    private const string PrimaryControlId = "primary";
    private const string SubmitIntent = "submit";
    private const string EventStreamMediaType = "text/event-stream";
    private const string CacheControlNoCache = "no-cache";
    private const string OpenTelemetryMarker = "OpenTelemetry";

    [Fact(DisplayName = "HTTP open-scene reaches IDigitalBrain and journals SceneOpened")]
    public async Task HttpOpenSceneJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UiFixture.DefaultShellName);

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            UiEdgeSse.OpenScene(UiFixture.DefaultShellName),
            new OpenSceneRequest(HomeSceneKey, HomeTitle),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(HomeSceneKey, opened.Synapse.SceneKey);
        Assert.Equal(HomeTitle, opened.Synapse.Title);
        Assert.Equal(shell.Id, opened.Synapse.Shell);
    }

    [Fact(DisplayName = "HTTP control activation journals ControlActivated on the scene")]
    public async Task HttpControlActivationJournalsControlActivated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>(HomeSceneKey);

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            UiEdgeSse.ActivateControl(HomeSceneKey, PrimaryControlId),
            new ActivateControlRequest(SubmitIntent),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var activation = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal(HomeSceneKey, activation.Synapse.SceneKey);
        Assert.Equal(PrimaryControlId, activation.Synapse.ControlId);
        Assert.Equal(SubmitIntent, activation.Synapse.Intent);
    }

    [Fact(DisplayName = "SSE shell events projects SceneOpened after open without process restart")]
    public async Task HttpShellEventsProjectsSceneOpenedWithoutRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UiFixture.DefaultShellName);

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app, streaming: true);
        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        using var openResponse = await http.PostAsJsonAsync(
            UiEdgeSse.OpenScene(UiFixture.DefaultShellName),
            new OpenSceneRequest(HomeSceneKey, HomeTitle),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(HomeSceneKey, journaled.Synapse.SceneKey);

        var projected = await UiEdgeSse.ReadNextSceneOpenedAsync(events.Reader, cancellationToken);

        Assert.Equal(HomeSceneKey, projected.SceneKey);
        Assert.Equal(HomeTitle, projected.Title);
        Assert.Equal(journaled.Sequence, projected.Sequence);
    }

    [Fact(DisplayName = "IDigitalBrain mutator journals SceneOpened and SSE projects without restart")]
    public async Task DigitalBrainMutatorJournalsAndSseProjectsWithoutRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UiFixture.DefaultShellName);
        var command = new OpenScene(CommandId.New(), SettingsSceneKey, SettingsTitle);

        await using var app = await StartUiEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app, streaming: true);
        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        await test.Client.Get<IShell>(UiFixture.DefaultShellName).Open(command);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, journaled.Synapse.CommandId);
        Assert.Equal(SettingsSceneKey, journaled.Synapse.SceneKey);
        Assert.Equal(SettingsTitle, journaled.Synapse.Title);
        Assert.Equal(shell.Id, journaled.Synapse.Shell);

        var projected = await UiEdgeSse.ReadNextSceneOpenedAsync(events.Reader, cancellationToken);

        Assert.Equal(journaled.Sequence, projected.Sequence);
        Assert.Equal(SettingsSceneKey, projected.SceneKey);
        Assert.Equal(SettingsTitle, projected.Title);
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
            new Uri(UiEdgeSse.ShellEvents(UiFixture.DefaultShellName, afterSequence: -1), UriKind.Relative),
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
            type => (type.FullName ?? type.Name).Contains(OpenTelemetryMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(System.Diagnostics.Activity), parameters);
        Assert.DoesNotContain(typeof(System.Diagnostics.ActivitySource), parameters);
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
        app.MapUiHost();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static async Task<ShellEventStream> OpenShellEventStreamAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            UiEdgeSse.ShellEvents(UiFixture.DefaultShellName, afterSequence: 0));
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

        Assert.Equal(EventStreamMediaType, streamResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            CacheControlNoCache,
            streamResponse.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var body = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        return new ShellEventStream(streamResponse, body);
    }

    private static HttpClient CreateClient(WebApplication app, bool streaming = false)
    {
        var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        if (streaming)
        {
            http.Timeout = TimeSpan.FromSeconds(30);
        }

        return http;
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
