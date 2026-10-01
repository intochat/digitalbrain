using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Agents.Signals;
using DigitalBrain.Modules.AI.Tests.Unit;

namespace DigitalBrain.Modules.AI.Tests.E2E;

public sealed class HostedAgentFacts
{
    [Fact]
    public async Task SeparateHostRunsRealProviderAdapterAndCommitsConversationBeforeSignal()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = deadline.Token;
        using var endpoint = new LoopbackServer();
        await using var brain = await E2ETest.Create().WithModule<AIModule, AIOptions>(options =>
            {
                options.Default.Profile = "fixture";
                options.ModelProfiles.Add("fixture", new AIModelProfileOptions
                {
                    Provider = "OpenAI",
                    Model = "fixture-model",
                    Endpoint = endpoint.Url,
                    Capabilities = LlmCapabilities.None,
                });
            })
            .WithExecution(new TestExecutionOptions
            {
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    ["DigitalBrain:Integrations:openai:ApiKey"] = "test-only",
                    ["DigitalBrain:Integrations:openai:Endpoint"] = endpoint.Url,
                },
            }).StartAsync(ct);
        var agent = brain.Get<IAgent>("hosted-assistant");
        await using var replied = await brain.Observe<AgentReplied>(agent, ct);
        var firstRequest = ReplyWithCompletion(endpoint, "first answer", ct);
        var firstResponse = agent.GetResponse("first question", ct);
        Assert.Equal("first answer", (await replied.NextAsync(ct: ct)).Text);
        Assert.Equal(2, (await agent.GetHistory(ct)).Count);
        Assert.Equal("first answer", await firstResponse);
        using (var request = JsonDocument.Parse((await firstRequest).Body))
        {
            Assert.Equal("fixture-model", request.RootElement.GetProperty("model").GetString());
        }

        var secondRequest = ReplyWithCompletion(endpoint, "second answer", ct);
        Assert.Equal("second answer", await agent.GetResponse("follow up", ct));
        using (var request = JsonDocument.Parse((await secondRequest).Body))
        {
            var messages = request.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Contains(messages, m => m.GetProperty("role").GetString() == "assistant"
                && m.GetProperty("content").ToString().Contains("first answer", StringComparison.Ordinal));
        }
        Assert.Equal(4, (await agent.GetHistory(ct)).Count);

        var inferenceRequest = ReplyWithCompletion(endpoint, "standalone", ct);
        var result = await brain.Get<ILLM>("fixture").Generate(
            new InferenceRequest([new AiMessage("user", [new AiText("independent")])]), cancellationToken: ct);
        Assert.Equal("standalone", Assert.IsType<AiText>(Assert.Single(Assert.Single(result.Messages).Content)).Text);
        await inferenceRequest;
        Assert.Equal(4, (await agent.GetHistory(ct)).Count);
    }

    private static Task<CapturedRequest> ReplyWithCompletion(LoopbackServer endpoint, string text, CancellationToken ct) =>
        endpoint.ReplyOnce("application/json", JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"),
            @object = "chat.completion",
            created = 1,
            model = "fixture-model",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 2, completion_tokens = 1, total_tokens = 3 },
        }), ct);
}
