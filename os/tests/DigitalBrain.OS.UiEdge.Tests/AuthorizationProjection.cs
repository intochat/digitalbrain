using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;
using Xunit;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class AuthorizationProjection(UiEdgeFixture fixture)
{
    private static readonly JsonSerializerOptions EventJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact(DisplayName =
        "SSE authorization feed projects AuthorizationRequired with provider display name and sign-in URL")]
    public async Task ProjectsAuthorizationRequiredFromJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateStreamingClient(app);
        await using var events = await OpenAuthorizationStreamAsync(http, afterSequence: 0, cancellationToken);

        var commandId = CommandId.New();
        var signInUrl = new Uri("https://ui.test.digitalbrain.local/oauth/mcp/authorize?state=sign-in-state");
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                commandId,
                "google.gmail",
                "DigitalBrain Gmail",
                signInUrl,
                "sign-in-state"),
            cancellationToken);
        var journaled = (await requiredWait).Synapse;

        var projected = await ReadNextAuthorizationAsync(events.Reader, cancellationToken);

        Assert.Equal(nameof(AuthorizationRequired), projected.Kind);
        Assert.Equal(journaled.CommandId.ToString(), projected.CommandId);
        Assert.Equal("google.gmail", projected.ServerKey);
        Assert.Equal("DigitalBrain Gmail", projected.ServerDisplayName);
        Assert.Equal(signInUrl.AbsoluteUri, projected.SignInUrl);
        Assert.Equal("sign-in-state", projected.State);
        Assert.True(projected.Sequence > 0);
    }

    [Fact(DisplayName =
        "SSE authorization feed projects AuthorizationCompleted and AuthorizationDenied so the card can resolve")]
    public async Task ProjectsAuthorizationResolutionFromJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        await using var app = await UiEdgeFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = CreateStreamingClient(app);
        await using var events = await OpenAuthorizationStreamAsync(http, afterSequence: 0, cancellationToken);

        var completeCommand = CommandId.New();
        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                completeCommand,
                "google.gmail",
                "DigitalBrain Gmail",
                new Uri("https://ui.test.digitalbrain.local/oauth/mcp/authorize?state=complete-state"),
                "complete-state"),
            cancellationToken);
        _ = await ReadNextAuthorizationAsync(events.Reader, cancellationToken);

        var completedWait = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback("complete-state", "auth-code", Error: null, Iss: null),
            cancellationToken);
        _ = await completedWait;

        var completed = await ReadNextAuthorizationAsync(events.Reader, cancellationToken);
        Assert.Equal(nameof(AuthorizationCompleted), completed.Kind);
        Assert.Equal(completeCommand.ToString(), completed.CommandId);
        Assert.Equal("complete-state", completed.State);
        Assert.Null(completed.SignInUrl);

        var denyCommand = CommandId.New();
        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                denyCommand,
                "salesforce",
                "DigitalBrain Salesforce",
                new Uri("https://ui.test.digitalbrain.local/oauth/mcp/authorize?state=deny-state"),
                "deny-state"),
            cancellationToken);
        _ = await ReadNextAuthorizationAsync(events.Reader, cancellationToken);

        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback("deny-state", Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
        _ = await deniedWait;

        var denied = await ReadNextAuthorizationAsync(events.Reader, cancellationToken);
        Assert.Equal(nameof(AuthorizationDenied), denied.Kind);
        Assert.Equal(denyCommand.ToString(), denied.CommandId);
        Assert.Equal("deny-state", denied.State);
    }

    [Fact(DisplayName = "UiEdgeContract names the authorization events path and event")]
    public void AuthorizationVocabularyIsNamedOnTheContract()
    {
        Assert.Equal("/authorizations/events", UiEdgeContract.AuthorizationEventsPath);
        Assert.Equal("authorization", UiEdgeContract.AuthorizationEvent);
    }

    private static HttpClient CreateStreamingClient(WebApplication app)
        => new()
        {
            BaseAddress = new Uri(app.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(30),
        };

    private static async Task<AuthorizationEventStream> OpenAuthorizationStreamAsync(
        HttpClient http,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{UiEdgeContract.AuthorizationEventsPath}?{UiEdgeContract.AfterSequenceQuery}={afterSequence}");
        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            UiEdgeContract.EventStreamContentType,
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new AuthorizationEventStream(response, body);
    }

    private static async Task<AuthorizationEvent> ReadNextAuthorizationAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(15));

        string? dataLine = null;
        string? eventName = null;
        while (!linked.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(linked.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':', StringComparison.Ordinal))
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

            if (line.Length == 0
                && string.Equals(eventName, UiEdgeContract.AuthorizationEvent, StringComparison.Ordinal)
                && dataLine is not null)
            {
                return JsonSerializer.Deserialize<AuthorizationEvent>(dataLine, EventJson)
                    ?? throw new InvalidOperationException("authorization SSE payload did not deserialize.");
            }
        }

        throw new TimeoutException("SSE stream ended before an authorization projection arrived.");
    }

    private sealed class AuthorizationEventStream : IAsyncDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _body;

        public AuthorizationEventStream(HttpResponseMessage response, Stream body)
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
