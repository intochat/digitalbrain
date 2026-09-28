using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.AI;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Workspace;

namespace IntoChat.Tests.E2E.Apps;

public sealed class AssistantNeuronUiFacts
{
    [Fact(Timeout = 180_000)]
    public async Task OpeningAssistantCreatesIndependentPrimitiveWindowsAndBoundButtonsExecuteTurns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var model = await ScriptedModelServer.StartAsync(ct);
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, model.Endpoint)).StartAsync(ct);
        await LeadData.SeedAsync(brain, "Neuron UI", ct);
        async Task<JsonElement> Open()
        {
            using var response = await brain.HttpClient.PostAsJsonAsync("/workspaces/neuron-ui/applications/assistant/open", new { draft = "Show active leads" }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        Task<JsonElement> Read(string kind, string name) => brain.HttpClient.GetFromJsonAsync<JsonElement>(
            "/workspaces/neuron-ui/apps/node?kind=" + kind + "&name=" + Uri.EscapeDataString(name), ct);
        async Task<Dictionary<string, (string Kind, JsonElement State)>> Tree(JsonElement window)
        {
            var nodes = new Dictionary<string, (string, JsonElement)>();
            async Task Visit(string kind, string name)
            {
                var state = await Read(kind, name);
                Assert.True(nodes.TryAdd(name, (kind, state)), "Duplicate primitive " + name);
                Assert.NotEqual("uichat", kind);
                var definition = state.TryGetProperty("definition", out var nested) ? nested : state;
                if (definition.TryGetProperty("children", out var children))
                {
                    foreach (var child in children.EnumerateArray())
                    { await Visit(child.GetProperty("kind").GetString()!, child.GetProperty("name").GetString()!); }
                }
            }
            await Visit("surface", window.GetProperty("surface").GetProperty("name").GetString()!);
            return nodes;
        }
        var first = await Open();
        var second = await Open();
        Assert.NotEqual(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal("surface", first.GetProperty("kind").GetString());
        var firstTree = await Tree(first);
        var secondTree = await Tree(second);
        string Part(Dictionary<string, (string Kind, JsonElement State)> tree, string part, string kind)
            => Assert.Single(tree, node => node.Value.Kind == kind && node.Key.EndsWith("/" + part, StringComparison.Ordinal)).Key;
        foreach (var tree in new[] { firstTree, secondTree })
        {
            foreach (var control in new[] { ("threads", "select"), ("model", "select"), ("draft", "textfield"),
                ("voice", "voiceinput"), ("attachments", "fileinput"), ("send", "button"), ("stop", "button"), ("new-conversation", "button") })
            { Part(tree, control.Item1, control.Item2); }
        }
        var draft = Part(firstTree, "draft", "textfield");
        var send = Part(firstTree, "send", "button");
        var otherDraft = Part(secondTree, "draft", "textfield");
        Assert.Equal("Show active leads", firstTree[draft].State.GetProperty("value").GetString());
        using var submitted = await brain.HttpClient.PostAsJsonAsync("/workspaces/neuron-ui/apps/event", new { kind = "button", name = send }, ct);
        submitted.EnsureSuccessStatusCode();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        Dictionary<string, (string Kind, JsonElement State)> completed;
        while (true)
        {
            await Task.Delay(100, deadline.Token);
            completed = await Tree(first);
            var visible = string.Join("\n", completed.Values.Select(node => node.State.ToString()));
            var receipts = completed[Part(completed, "receipts", "layout")].State.GetProperty("definition").GetProperty("children");
            if (visible.Contains("Opened Active leads", StringComparison.Ordinal) && receipts.GetArrayLength() > 0
                && !completed[Part(completed, "stop", "button")].State.GetProperty("enabled").GetBoolean()) { break; }
        }
        var results = completed[Part(completed, "results", "layout")].State.GetProperty("definition").GetProperty("children");
        Assert.NotEmpty(results.EnumerateArray());
        Assert.Contains(completed, node => node.Value.Kind == "button" && node.Value.State.GetProperty("label").GetString() == "Open");
        var untouched = await Tree(second);
        Assert.Empty(untouched[Part(untouched, "messages", "layout")].State.GetProperty("definition").GetProperty("children").EnumerateArray());
        Assert.Equal("Show active leads", (await Read("textfield", otherDraft)).GetProperty("value").GetString());
        model.AssertCompleted();
    }
}
