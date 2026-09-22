using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Agents.Signals;

namespace DigitalBrain.Tests;

public sealed class HostedAgentFacts
{
    [Fact]
    public async Task SeparateHostRunsRealProviderAdapterAndCommitsConversationBeforeSignal()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = deadline.Token;
        using var endpoint = new Loopback();
        var options = new AIOptions { Default = new() { Profile = "fixture" } };
        options.ModelProfiles.Add("fixture", new AIModelProfileOptions
        {
            Provider = "OpenAI",
            Model = "fixture-model",
            Endpoint = endpoint.Url,
            Capabilities = LlmCapabilities.None,
        });
        await using var brain = await E2ETest.Create().WithModule<AIModule>(m => m.WithOptions(options))
            .WithExecution(new TestExecutionOptions
            {
                PrivateConfiguration = new Dictionary<string, string?> { ["DigitalBrain:AI:OpenAI:ApiKey"] = "test-only" },
            }).StartAsync(ct);
        var agent = brain.Get<IAgent>("hosted-assistant");
        await using var replied = await brain.Observe<AgentReplied>(agent, ct);
        var firstRequest = endpoint.Reply("first answer", ct);
        var firstResponse = agent.GetResponse("first question", ct);
        Assert.Equal("first answer", (await replied.NextAsync(ct: ct)).Text);
        Assert.Equal(2, (await agent.GetHistory(ct)).Count);
        Assert.Equal("first answer", await firstResponse);
        using (var request = JsonDocument.Parse(await firstRequest))
        {
            Assert.Equal("fixture-model", request.RootElement.GetProperty("model").GetString());
        }

        var secondRequest = endpoint.Reply("second answer", ct);
        Assert.Equal("second answer", await agent.GetResponse("follow up", ct));
        using (var request = JsonDocument.Parse(await secondRequest))
        {
            var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Contains(messages, m => m.GetProperty("role").GetString() == "assistant"
                && m.GetProperty("content").ToString().Contains("first answer", StringComparison.Ordinal));
        }
        Assert.Equal(4, (await agent.GetHistory(ct)).Count);

        var inferenceRequest = endpoint.Reply("standalone", ct);
        var result = await brain.Get<ILLM>("fixture").Generate(
            new InferenceRequest([new AiMessage("user", [new AiText("independent")])]), cancellationToken: ct);
        Assert.Equal("standalone", Assert.IsType<AiText>(Assert.Single(Assert.Single(result.Messages).Content)).Text);
        await inferenceRequest;
        Assert.Equal(4, (await agent.GetHistory(ct)).Count);
    }

    private sealed class Loopback : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }
        public Loopback()
        {
            using var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
        }
        public async Task<string> Reply(string text, CancellationToken ct)
        {
            var context = await _listener.GetContextAsync().WaitAsync(ct);
            using var reader = new StreamReader(context.Request.InputStream);
            var request = await reader.ReadToEndAsync(ct);
            var response = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString("N"),
                @object = "chat.completion",
                created = 1,
                model = "fixture-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 2, completion_tokens = 1, total_tokens = 3 },
            });
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, ct);
            context.Response.Close();
            return request;
        }
        public void Dispose() => _listener.Close();
    }
}