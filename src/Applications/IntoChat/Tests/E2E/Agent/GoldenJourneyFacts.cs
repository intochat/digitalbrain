using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Testing.E2E;
using IntoChat.Agent;
using IntoChat.Workspace;
using Npgsql;

namespace IntoChat.Tests.E2E.Agent;

// Golden journeys J1 and J2a from the product spec section 10, run against a live model. The suite
// is gated by DIGITALBRAIN_E2E_LIVE_MODEL (gate G-7): without the flag and an API key it skips
// cleanly instead of faking a pass. Each journey repeats 20 times and must succeed at least 18.
public sealed class GoldenJourneyFacts
{
    private const int Attempts = 20;
    private const int RequiredPasses = 18;
    private static readonly string[] FormTools = ["show_form", "show_view"];

    public static bool LiveEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_LIVE_MODEL") == "1";

    [Fact(Timeout = 7_200_000, SkipUnless = nameof(LiveEnabled), Skip = "Set DIGITALBRAIN_E2E_LIVE_MODEL=1 and DIGITALBRAIN_E2E_MODEL_API_KEY to run the live golden journeys.")]
    [Trait("Category", "LiveModel")]
    public async Task J1CustomersJourneyPassesGoldenThreshold()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartLiveAsync(ct);
        await SeedCustomersAsync(brain, ct);
        var report = await RepeatAsync(async (attempt, token) =>
        {
            var workspaceId = $"j1-{attempt:D2}";
            var scope = WorkspaceScope.Create("owner", workspaceId);
            var workspace = brain.Get<IWorkspace>(scope.Id);

            await AskAsync(brain, workspaceId, "thread", $"{workspaceId}-open", "Show me all customers", token);
            var opened = Assert.Single((await workspace.Read()).Windows);
            var tableId = opened.Reference.NeuronId;
            var allCustomers = await brain.Get<ISupabaseTable>(tableId).Read(new(0, 25));
            Assert.NotNull(allCustomers);
            Assert.True(allCustomers!.TotalRows > 0, "The customers window opened with no rows.");

            await AskAsync(brain, workspaceId, "thread", $"{workspaceId}-london", "only London", token);
            var refinedWindow = Assert.Single((await workspace.Read()).Windows);
            Assert.Equal(tableId, refinedWindow.Reference.NeuronId);
            var london = await brain.Get<ISupabaseTable>(tableId).Read(new(0, 25));
            Assert.NotNull(london);
            Assert.NotEmpty(london!.Filters);
            Assert.True(london.FilteredRows > 0, "The seeded data has no London customers.");
            Assert.True(london.FilteredRows < london.TotalRows, "only London did not narrow the same window.");

            await AskAsync(brain, workspaceId, "thread", $"{workspaceId}-count", "how many?", token);
            var conversation = await brain.Get<IAgent>(AgentEndpoints.ConversationKey(scope.Id, "thread")).ReadConversation(token);
            var answer = conversation.Turns[^1].AssistantText;
            Assert.Contains(london.FilteredRows.ToString(), answer, StringComparison.Ordinal);
            Assert.Single((await workspace.Read()).Windows);
        }, ct);
        Assert.True(report.Passes >= RequiredPasses, report.Describe());
    }

    [Fact(Timeout = 7_200_000, SkipUnless = nameof(LiveEnabled), Skip = "Set DIGITALBRAIN_E2E_LIVE_MODEL=1 and DIGITALBRAIN_E2E_MODEL_API_KEY to run the live golden journeys.")]
    [Trait("Category", "LiveModel")]
    public async Task J2aCardFormJourneyPassesGoldenThreshold()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartLiveAsync(ct);
        var report = await RepeatAsync(async (attempt, token) =>
        {
            var workspaceId = $"j2a-{attempt:D2}";
            var scope = WorkspaceScope.Create("owner", workspaceId);
            var workspace = brain.Get<IWorkspace>(scope.Id);

            var drawn = await AskAsync(brain, workspaceId, "thread", $"{workspaceId}-draw",
                "Draw a card with name, surname and date of birth", token);
            AssertFormTurn(drawn);
            Assert.NotEmpty((await workspace.Read()).Windows);

            var password = await AskAsync(brain, workspaceId, "thread", $"{workspaceId}-password",
                "Add a password field", token);
            AssertFormTurn(password);
            Assert.NotEmpty((await workspace.Read()).Windows);
        }, ct);
        Assert.True(report.Passes >= RequiredPasses, report.Describe());
    }

    private static void AssertFormTurn(string stream)
    {
        var toolNames = AgentEvents(stream)
            .Where(ev => Text(ev, "type") == "TOOL_CALL_START")
            .Select(ev => Text(ev, "toolCallName"))
            .Where(name => name is not null)
            .ToArray();
        Assert.Contains(toolNames, name => FormTools.Contains(name, StringComparer.Ordinal));
        Assert.DoesNotContain(toolNames, name => name!.StartsWith("code_", StringComparison.Ordinal) || name.StartsWith("behavior_", StringComparison.Ordinal));
    }

    private static async Task<E2EBrain> StartLiveAsync(CancellationToken ct)
    {
        var key = Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(key), "Live model verification requires DIGITALBRAIN_E2E_MODEL_API_KEY.");
        var endpoint = new Uri(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_ENDPOINT") ?? "https://api.openai.com/v1/");
        return await IntoChatE2ETest.Create(key!)
            .ConfigureModule<AIModule>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, endpoint))
            .StartAsync(ct);
    }

    private static async Task SeedCustomersAsync(E2EBrain brain, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "CREATE TABLE customers (id int PRIMARY KEY, company text, city text); " +
            "INSERT INTO customers SELECT n, 'Customer ' || n, CASE WHEN n % 10 = 0 THEN 'London' ELSE 'Berlin' END FROM generate_series(1,60) n",
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> AskAsync(E2EBrain brain, string workspaceId, string threadId, string runId, string message, CancellationToken ct)
    {
        using var response = await brain.HttpClient.PostAsJsonAsync("/agent",
            new { workspaceId, threadId, runId, messages = new[] { new { role = "user", content = message } } }, ct);
        var stream = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("RUN_FINISHED", stream, StringComparison.Ordinal);
        Assert.DoesNotContain("RUN_ERROR", stream, StringComparison.Ordinal);
        return stream;
    }

    private static async Task<JourneyReport> RepeatAsync(Func<int, CancellationToken, Task> attempt, CancellationToken ct)
    {
        var passes = 0;
        var failures = new List<string>();
        for (var number = 1; number <= Attempts; number++)
        {
            ct.ThrowIfCancellationRequested();
            try { await attempt(number, ct); passes++; }
            catch (Exception error) { failures.Add($"#{number}: {error.Message}"); }
        }
        return new JourneyReport(passes, failures);
    }

    private static IReadOnlyList<JsonElement> AgentEvents(string stream)
    {
        var events = new List<JsonElement>();
        foreach (var line in stream.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("data: ", StringComparison.Ordinal)) { continue; }
            using var document = JsonDocument.Parse(trimmed["data: ".Length..]);
            events.Add(document.RootElement.Clone());
        }
        return events;
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record JourneyReport(int Passes, IReadOnlyList<string> Failures)
    {
        public string Describe() =>
            $"{Passes}/{Attempts} attempts passed (need {RequiredPasses}). Failures:\n{string.Join("\n", Failures)}";
    }
}