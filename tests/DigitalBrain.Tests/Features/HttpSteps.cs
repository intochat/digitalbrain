using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.UI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class HttpSteps(BrainWorld world, UiChatSteps uiChat, UiSteps ui) : IAsyncDisposable
{
    private BrainHttp? _http;
    private HttpResponseMessage? _response;
    private string _body = string.Empty;
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

    [Then(@"the response body has ""(.*)"" of ""(.*)""")]
    public void BodyProperty(string property, string value)
        => Assert.Equal(value, JsonNode.Parse(_body)?[property]?.GetValue<string>());

    [Then(@"GET ""(.*)"" as ""(.*)"" with ""(.*)"" returns (\d+)")]
    public async Task AuthenticatedStatus(string path, string username, string password, int status)
    {
        await GetAuthenticated(path, username, password);
        Status(status);
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

    [Given(@"chart ""(.*)"" renders ""(.*)""")]
    public Task RenderChart(string name, string title) => ui.Render(name, title);

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
