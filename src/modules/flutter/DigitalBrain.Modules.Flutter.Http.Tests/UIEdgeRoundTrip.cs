using System.Net;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class UIEdgeRoundTrip(UIFixture fixture)
{
    private const string HomeSceneKey = "home";
    private const string HomeTitle = "Home";
    private const string SettingsSceneKey = "settings";
    private const string SettingsTitle = "Settings";
    private const string PrimaryControlId = "primary";
    private const string SubmitIntent = "submit";

    [Fact(DisplayName = "HTTP open-scene reaches IDigitalBrain and journals SceneOpened")]
    public async Task HttpOpenSceneJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UIFixture.DefaultShellName);

        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            UIEdgeSse.OpenScene(UIFixture.DefaultShellName),
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

        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.PostAsJsonAsync(
            UIEdgeSse.ActivateControl(HomeSceneKey, PrimaryControlId),
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
        var shell = test.Neuron<IShell>(UIFixture.DefaultShellName);

        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app, streaming: true);
        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        using var openResponse = await http.PostAsJsonAsync(
            UIEdgeSse.OpenScene(UIFixture.DefaultShellName),
            new OpenSceneRequest(HomeSceneKey, HomeTitle),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, openResponse.StatusCode);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(HomeSceneKey, journaled.Synapse.SceneKey);

        var projected = await UIEdgeSse.ReadNextSceneOpenedAsync(events.Reader, cancellationToken);

        Assert.Equal(HomeSceneKey, projected.SceneKey);
        Assert.Equal(HomeTitle, projected.Title);
        Assert.Equal(journaled.Sequence, projected.Sequence);
    }

    [Fact(DisplayName = "IDigitalBrain mutator journals SceneOpened and SSE projects without restart")]
    public async Task DigitalBrainMutatorJournalsAndSseProjectsWithoutRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(UIFixture.DefaultShellName);
        var command = new OpenScene(CommandId.New(), SettingsSceneKey, SettingsTitle);

        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app, streaming: true);
        await using var events = await OpenShellEventStreamAsync(http, cancellationToken);

        await test.Client.Get<IShell>(UIFixture.DefaultShellName).Open(command);

        var journaled = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, journaled.Synapse.CommandId);
        Assert.Equal(SettingsSceneKey, journaled.Synapse.SceneKey);
        Assert.Equal(SettingsTitle, journaled.Synapse.Title);
        Assert.Equal(shell.Id, journaled.Synapse.Shell);

        var projected = await UIEdgeSse.ReadNextSceneOpenedAsync(events.Reader, cancellationToken);

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

        await using var app = await UIFixture.StartUIEdgeAsync(test, cancellationToken);
        using var http = CreateClient(app);

        using var response = await http.GetAsync(
            new Uri(UIEdgeSse.ShellEvents(UIFixture.DefaultShellName, afterSequence: -1), UriKind.Relative),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<ShellEventStream> OpenShellEventStreamAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var streamRequest = new HttpRequestMessage(
            HttpMethod.Get,
            UIEdgeSse.ShellEvents(UIFixture.DefaultShellName, afterSequence: 0));
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

        Assert.Equal(
            UIEdgeContract.EventStreamContentType,
            streamResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            UIEdgeContract.CacheControlNoCache,
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
