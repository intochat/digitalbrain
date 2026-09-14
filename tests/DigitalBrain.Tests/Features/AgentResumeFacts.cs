using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace DigitalBrain.Tests;

// Phase 2 spike S4: a slot that stops mid-answer leaves the client with a transport error and no terminal
// frame. The shell re-issues that run; this is the kernel's half of it.
public sealed class AgentResumeFacts
{
    [Fact]
    public async Task A_resumed_run_replays_the_answer_without_a_second_model_call()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("The rename landed on coding/rename-1.");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();
        var threadId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        var first = await PostAsync(client, threadId, runId, "Rename Greet to Hello", resume: false);
        Assert.Equal("The rename landed on coding/rename-1.", Answer(first));

        var resumed = await PostAsync(client, threadId, runId, "Rename Greet to Hello", resume: true);

        Assert.Equal(Answer(first), Answer(resumed));
        Assert.Equal("RUN_FINISHED", resumed[^1].GetProperty("type").GetString());
        // One model call for two posts: the reconnect was answered from the ledger.
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task A_resume_of_a_run_this_silo_never_saw_is_answered_as_a_fresh_run()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("Promoted slot b.");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();

        // This is the promotion case: the new slot has neither the ledger nor the session of the old one.
        var resumed = await PostAsync(client, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Rebuild and promote", resume: true);

        Assert.Equal("Promoted slot b.", Answer(resumed));
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task A_repeated_run_without_the_resume_flag_is_answered_again_by_the_model()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("first");
        model.Say("second");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();
        var threadId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        var first = await PostAsync(client, threadId, runId, "Say something", resume: false);
        var second = await PostAsync(client, threadId, runId, "Say something", resume: false);

        Assert.Equal("first", Answer(first));
        Assert.Equal("second", Answer(second));
        Assert.Equal(2, model.Calls.Count);
    }

    private static string Answer(List<JsonElement> events) => string.Concat(events
        .Where(item => item.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT")
        .Select(item => item.GetProperty("delta").GetString()));

    private static async Task<List<JsonElement>> PostAsync(HttpClient client, string threadId, string runId, string text, bool resume)
    {
        using var response = await client.PostAsJsonAsync("/agent", new
        {
            threadId,
            runId,
            messages = new[] { new { id = Guid.NewGuid().ToString(), role = "user", content = text } },
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            state = new { },
            forwardedProps = resume
                ? new Dictionary<string, object> { ["resume"] = true }
                : new Dictionary<string, object>(),
        }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, body);
        return body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[5..])).ToList();
    }
}
