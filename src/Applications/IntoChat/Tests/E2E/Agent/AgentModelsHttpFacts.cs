using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;

namespace IntoChat.Tests.E2E.Agent;

public sealed class AgentModelsHttpFacts
{
    [Fact(Timeout = 240_000)]
    public async Task CatalogIsGatedAndUnknownSelectionDoesNotCreateATurn()
    {
        var ct = TestContext.Current.CancellationToken;
        const string apiKey = "model-catalog-secret-canary";
        await using var brain = await IntoChatE2ETest.Create(modelApiKey: apiKey)
            .WithResourceEnvironment(new Dictionary<string, string>
            {
                ["DigitalBrain__Auth__Username"] = "owner",
                ["DigitalBrain__Auth__Password"] = "catalog-test-password",
            }).StartAsync(ct);

        using var anonymous = await brain.HttpClient.GetAsync("/ai/models", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        brain.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:catalog-test-password")));
        using var response = await brain.HttpClient.GetAsync("/ai/models", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(apiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint", body, StringComparison.OrdinalIgnoreCase);
        using var catalog = JsonDocument.Parse(body);
        Assert.True(catalog.RootElement.GetProperty("automatic").GetProperty("available").GetBoolean());
        Assert.Contains(catalog.RootElement.GetProperty("models").EnumerateArray(),
            model => model.GetProperty("id").GetString() == "preset:IGpt56Luna");

        using var rejected = await brain.HttpClient.PostAsJsonAsync("/agent", new
        {
            workspaceId = "model-validation", threadId = "thread", runId = "unknown-model",
            modelProfile = "profile:does-not-exist",
            messages = new[] { new { role = "user", content = "Hello" } },
        }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("application/json", rejected.Content.Headers.ContentType?.MediaType);
        using var failure = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(ct));
        Assert.Equal("MODEL_UNAVAILABLE", failure.RootElement.GetProperty("code").GetString());
        var conversation = await brain.HttpClient.GetFromJsonAsync<AgentConversationState>(
            "/workspaces/model-validation/conversations/thread", ct);
        Assert.NotNull(conversation);
        Assert.Null(conversation.ActiveRunId);
        Assert.Empty(conversation.Turns);
        Assert.Equal(0, conversation.Revision);
    }
}
