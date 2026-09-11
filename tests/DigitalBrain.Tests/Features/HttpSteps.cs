using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class HttpSteps(BrainWorld world, UiChatSteps uiChat, KitSteps kit) : IAsyncDisposable
{
    private BrainHttp? _http;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;
    private SignalId? _work;
    private string? _reset;
    private readonly ConcurrentQueue<(string? EventName, string Data)> _frames = new();
    private readonly CancellationTokenSource _streamCancellation = new();
    private Task? _streamReader;

    private HttpClient Client => (_http ?? throw new InvalidOperationException("Start the HTTP brain first.")).Client;

    [Given("a running brain with AI and UI behind HTTP")]
    public Task Start() => StartWithCredentials(null, null);

    [Given(@"a running brain with AI and UI behind HTTP requiring ""(.*)"" and ""(.*)""")]
    public async Task StartWithCredentials(string? username, string? password)
    {
        await uiChat.GivenAiAndUi();
        _http = await BrainHttp.StartAsync(world.Brain, username, password);
    }

    [When(@"POST ""(.*)"" with (.*)$")]
    public async Task Post(string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        await Remember(await Client.PostAsync(path, content));
        var mediaType = _response!.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (JsonNode.Parse(_body) is JsonObject body && body["work"] is { } work)
            {
                _work = work.Deserialize<SignalId>(JsonSerializerOptions.Web);
            }
        }
    }

    [When(@"POST the cancel of the accepted work on chat ""(.*)""")]
    public async Task Cancel(string name)
    {
        Assert.NotNull(_work);
        await Remember(await Client.PostAsync($"/chats/{name}/turns/{_work}/cancel", null));
    }

    [When(@"GET ""([^""]*)""$")]
    public async Task Get(string path) => await Remember(await Client.GetAsync(path));

    [When(@"GET ""(.*)"" as ""(.*)"" with ""(.*)""")]
    public async Task GetAuthenticated(string path, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        await Remember(await Client.SendAsync(request));
    }

    [Then(@"the response status is (\d+)")]
    public void Status(int status)
    {
        Assert.NotNull(_response);
        Assert.Equal(status, (int)_response.StatusCode);
    }

    [Then(@"GET ""(.*)"" has user text ""(.*)""")]
    public async Task Transcript(string path, string text)
    {
        await Get(path);
        Status(200);
        AssertUserText(JsonNode.Parse(_body), text);
    }

    [Then(@"GET ""(.*)"" reports the accepted work as ""(.*)""")]
    public async Task Turn(string path, string status)
    {
        await Get(path);
        Assert.True(HasTurn(status), $"Work {_work} was not {status}: {_body}");
    }

    [When(@"the accepted work on chat ""(.*)"" becomes ""(.*)"" within (\d+) seconds")]
    public async Task WaitForTurn(string name, string status, int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            await Get($"/chats/{name}/turns");
            if (HasTurn(status))
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.Fail($"Work {_work} on chat {name} did not become {status} within {seconds}s: {_body}");
    }

    [Then(@"the response body has ""(.*)"" of ""(.*)""")]
    public void BodyProperty(string property, string value)
        => Assert.Equal(value, JsonNode.Parse(_body)?[property]?.GetValue<string>());

    [Then(@"GET ""(.*)"" as ""(.*)"" with ""(.*)"" returns (\d+)")]
    public async Task AuthenticatedStatus(string path, string username, string password, int status)
    {
        await GetAuthenticated(path, username, password);
        Status(status);
    }

    [When(@"the stream ""(.*)"" is read up to (\d+) seconds")]
    public async Task ReadStream(string path, int seconds)
    {
        _reset = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            using var response = await Client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await foreach (var frame in ReadFrames(response, timeout.Token))
            {
                if (frame.EventName == "reset")
                {
                    _reset = frame.Data;
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        finally
        {
            await timeout.CancelAsync();
        }
    }

    [When(@"the stream ""(.*)"" is opened")]
    public async Task OpenStream(string path)
    {
        var response = await Client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, _streamCancellation.Token);
        _streamReader = CollectFrames(response, _streamCancellation.Token);
    }

    [Then(@"the stream carries a ""(.*)"" saying ""(.*)"" within (\d+) seconds")]
    public Task StreamCarries(string eventName, string text, int seconds)
        => WaitForFrame(eventName, payload => payload["text"]?.GetValue<string>() == text
            || payload["signal"]?.GetValue<string>() == text, $"saying '{text}'", seconds);

    [Then(@"the stream carries a ""(.*)"" with status ""(.*)"" within (\d+) seconds")]
    public Task StreamCarriesStatus(string eventName, string status, int seconds)
        => WaitForFrame(eventName, payload => payload["status"]?.GetValue<string>() == status,
            $"with status '{status}'", seconds);

    private async Task WaitForFrame(string eventName, Func<JsonNode, bool> predicate, string expectation, int seconds)
    {
        Assert.NotNull(_streamReader);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (_frames.Any(frame =>
            {
                if (frame.EventName != eventName)
                {
                    return false;
                }

                var payload = JsonNode.Parse(frame.Data);
                return payload is not null && predicate(payload);
            }))
            {
                return;
            }

            if (_streamReader.IsCompleted)
            {
                await _streamReader;
                break;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"The stream did not carry {eventName} {expectation} within {seconds}s. Frames seen: {string.Join(Environment.NewLine, _frames)}");
    }

    private async Task CollectFrames(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            try
            {
                await foreach (var frame in ReadFrames(response, cancellationToken))
                {
                    _frames.Enqueue(frame);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Disposing the scenario stops the background stream reader.
            }
        }
    }

    private static async IAsyncEnumerable<(string? EventName, string Data)> ReadFrames(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        string? eventName = null;
        var data = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                yield return (eventName, string.Join('\n', data));
                eventName = null;
                data.Clear();
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].TrimStart(' ');
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.Add(line[5..].TrimStart(' '));
            }
        }
    }

    [Then(@"the stream reset carries user text ""(.*)""")]
    public void StreamText(string text)
    {
        Assert.NotNull(_reset);
        var turns = JsonNode.Parse(_reset)?["state"]?["turns"]?.AsArray();
        Assert.NotNull(turns);
        Assert.Contains(turns, turn => turn?["text"]?.GetValue<string>() == text);
    }

    [Given(@"chart ""(.*)"" renders ""(.*)""")]
    public Task RenderChart(string name, string title) => kit.Render(name, title);

    [Given(@"surface ""(.*)"" has opened ""(.*)"" with button ""(.*)""")]
    public async Task OpenSurface(string name, string key, string button)
    {
        var root = new SurfaceComponent("column", "root", Children: [new SurfaceComponent("button", button)]);
        await Post($"/surfaces/{name}/open", JsonSerializer.Serialize(new { surfaceKey = key, title = key, root }, JsonSerializerOptions.Web));
        Status(202);
        await UiWait.Until(async () =>
        {
            await Get($"/surfaces/{name}");
            Status(200);
            return JsonNode.Parse(_body)?["scenes"]?.AsArray().Any(scene => scene?["surfaceKey"]?.GetValue<string>() == key) == true;
        }, $"Surface {name} did not open scene {key} within 10 seconds.");
    }

    private bool HasTurn(string status)
    {
        Assert.NotNull(_work);
        Status(200);
        var turns = JsonSerializer.Deserialize<ChatTurns>(_body, JsonSerializerOptions.Web);
        Assert.NotNull(turns);
        return turns.Turns.Any(turn => turn.Turn == _work && turn.Status.ToString() == status);
    }

    private static void AssertUserText(JsonNode? transcript, string text)
    {
        Assert.NotNull(transcript);
        var turns = transcript["turns"]?.AsArray();
        Assert.NotNull(turns);
        Assert.Contains(turns, turn => turn?["fromUser"]?.GetValue<bool>() == true && turn["text"]?.GetValue<string>() == text);
    }

    private async Task Remember(HttpResponseMessage response)
    {
        _response?.Dispose();
        _response = response;
        _body = await response.Content.ReadAsStringAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _streamCancellation.CancelAsync();
        try
        {
            if (_streamReader is { } reader)
            {
                await reader;
            }
        }
        finally
        {
            _streamCancellation.Dispose();
            _response?.Dispose();
            if (_http is { } http)
            {
                await http.DisposeAsync();
            }
        }
    }
}
