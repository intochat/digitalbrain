using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class ChatStreamEndpoint(UIFixture fixture)
{
    private const string ChatName = "stream-edge";
    private const string FreshIdChatName = "stream-fresh-id";
    private const string Prompt = "stream this answer to the edge";
    private const int FactTimeout = 120_000;

    private static readonly JsonSerializerOptions AiJson = AIJsonUtilities.DefaultOptions;
    private static readonly TimeSpan HeaderBudget = TimeSpan.FromSeconds(15);

    [Fact(Timeout = FactTimeout, DisplayName =
        "POST /chats/{chatName}/messages/stream yields chat-delta frames of ChatResponseUpdate and journals one AssistantResponded")]
    public async Task StreamEndpointYieldsDeltasAndJournalsOneAnswer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var chat = test.Neuron<IChat>(ChatName);

        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(app.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(30),
        };

        var frames = await DrainStreamAsync(http, ChatName, Prompt, cancellationToken);

        Assert.True(
            frames.Count > 1,
            $"Expected multiple chat-delta frames so the edge streams, got {frames.Count}.");
        Assert.Equal(
            UIAssistantProbe.Answer,
            string.Concat(frames.Select(frame => frame.Text)));

        var journaled = await chat.Outgoing.ReadAsync<Synapse>(afterSequence: 0, cancellationToken: cancellationToken);
        Assert.Collection(
            journaled.Where(fact => fact.Synapse is UserMessaged or AssistantResponded),
            fact => Assert.Equal(Prompt, Assert.IsType<UserMessaged>(fact.Synapse).Text),
            fact => Assert.Equal(UIAssistantProbe.Answer, Assert.IsType<AssistantResponded>(fact.Synapse).Text));
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "the stream edge mints a fresh CommandId per request, pinned by two sequential posts")]
    public async Task StreamEndpointMintsAFreshCommandIdPerRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var chat = test.Neuron<IChat>(FreshIdChatName);

        await using var app = await UIFixture.StartUiHttpAsync(test, cancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(app.Urls.Single()),
            Timeout = TimeSpan.FromSeconds(30),
        };

        await DrainStreamAsync(http, FreshIdChatName, "first", cancellationToken);
        await DrainStreamAsync(http, FreshIdChatName, "second", cancellationToken);

        var messaged = (await chat.Outgoing.ReadAsync<UserMessaged>(
            afterSequence: 0, cancellationToken: cancellationToken))
            .Select(fact => fact.Synapse.CommandId)
            .ToArray();

        Assert.Equal(2, messaged.Length);
        Assert.NotEqual(messaged[0], messaged[1]);
        Assert.NotEqual(Guid.Empty, messaged[0].Value);
        Assert.NotEqual(Guid.Empty, messaged[1].Value);
    }

    [Fact(DisplayName =
        "UiHttpContract names the stream path and chat-delta event; vocabulary stays closed")]
    public void StreamPathAndDeltaEventAreNamedOnTheContract()
    {
        Assert.Equal("/chats/{chatName}/messages/stream", UiHttpContract.StreamMessagePath);
        Assert.Equal("chat-delta", UiHttpContract.ChatDeltaEvent);
        Assert.Equal(
            TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture),
            ChatDeltaFeed.TurnBudget);
    }

    private static async Task<List<ChatResponseUpdate>> DrainStreamAsync(
        HttpClient http,
        string chatName,
        string text,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            UiHttpContract.StreamMessagePath.Replace("{chatName}", chatName, StringComparison.Ordinal))
        {
            Content = JsonContent.Create(new SendMessageRequest(text)),
        };

        using var response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            UiHttpContract.EventStreamContentType,
            response.Content.Headers.ContentType?.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body, Encoding.UTF8);

        var frames = new List<ChatResponseUpdate>();
        string? eventName = null;
        string? dataLine = null;

        using var headerBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerBudget.CancelAfter(HeaderBudget);

        while (!headerBudget.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(headerBudget.Token);
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

            if (line.Length == 0 && dataLine is not null)
            {
                if (string.Equals(eventName, UiHttpContract.ChatDeltaEvent, StringComparison.Ordinal))
                {
                    var frame = JsonSerializer.Deserialize<ChatResponseUpdate>(dataLine, AiJson)
                        ?? throw new InvalidOperationException("chat-delta payload did not deserialize.");
                    frames.Add(frame);
                }

                eventName = null;
                dataLine = null;
            }
        }

        return frames;
    }
}
