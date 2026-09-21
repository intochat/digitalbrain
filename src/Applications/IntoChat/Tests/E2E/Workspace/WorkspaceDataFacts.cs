using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using IntoChat.Workspace;
using Npgsql;

namespace IntoChat.Tests.E2E.Workspace;

public sealed class WorkspaceDataFacts
{
    [Fact(Timeout = 180_000)]
    public async Task TableViewFiltersBeyondPageOneAndRejectsForeignWorkspaceAndStaleRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var connectionString = await brain.Application.GetConnectionStringAsync("supabase-database", ct);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var seed = new NpgsqlCommand("CREATE TABLE leads (id int, name text, active boolean); INSERT INTO leads SELECT n, CASE WHEN n = 55 THEN 'Beyond page one' ELSE 'Lead ' || n END, true FROM generate_series(1,60) n", connection);
        await seed.ExecuteNonQueryAsync(ct);
        var scope = WorkspaceScope.Create("owner", "workspace-a");
        var table = brain.Get<ISupabaseTable>("workspace-http-table");
        await table.CreateFromQuery(new("Leads", "select id, name, active from leads order by id"));
        await brain.Get<IWorkspace>(scope.Id).Open(new("open", "window", "Leads", new("workspace-http-table"), 0));
        var path = "/workspaces/workspace-a/tables/workspace-http-table";
        var first = await brain.HttpClient.GetFromJsonAsync<JsonElement>(path + "?offset=0&limit=25", ct);
        Assert.Equal(25, first.GetProperty("rows").GetArrayLength());
        Assert.Equal(60, first.GetProperty("totalRows").GetInt32());
        var secondPage = await brain.HttpClient.GetFromJsonAsync<JsonElement>(path + "?offset=25&limit=25", ct);
        Assert.Equal(26, secondPage.GetProperty("rows")[0].GetProperty("cells")[0].GetInt32());
        var idColumn = first.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("label").GetString() == "id").GetProperty("id").GetString();
        using var sortedResponse = await brain.HttpClient.PostAsJsonAsync(path + "/view", new
        {
            expectedRevision = first.GetProperty("revision").GetInt64(),
            filters = Array.Empty<object>(),
            sort = new { columnId = idColumn, descending = true },
            visibleColumns = first.GetProperty("visibleColumns").EnumerateArray().Select(c => c.GetString()).ToArray(),
        }, ct);
        Assert.Equal(HttpStatusCode.OK, sortedResponse.StatusCode);
        first = await sortedResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(60, first.GetProperty("rows")[0].GetProperty("cells")[0].GetInt32());
        var nameColumn = first.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("label").GetString() == "name").GetProperty("id").GetString();
        var update = new
        {
            expectedRevision = first.GetProperty("revision").GetInt64(),
            filters = new[] { new { columnId = nameColumn, @operator = "eq", value = "Beyond page one" } },
            sort = (object?)null,
            visibleColumns = first.GetProperty("visibleColumns").EnumerateArray().Select(c => c.GetString()).ToArray(),
        };
        using var changed = await brain.HttpClient.PostAsJsonAsync(path + "/view", update, ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var filtered = await changed.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, filtered.GetProperty("filteredRows").GetInt32());
        Assert.Equal(55, filtered.GetProperty("rows")[0].GetProperty("cells")[0].GetInt32());
        using var stale = await brain.HttpClient.PostAsJsonAsync(path + "/view", update, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var foreign = await brain.HttpClient.GetAsync("/workspaces/workspace-b/tables/workspace-http-table", ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var closedResponse = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/windows/window/close", new { expectedRevision = 1 }, ct);
        Assert.Equal(HttpStatusCode.OK, closedResponse.StatusCode);
        var closed = await closedResponse.Content.ReadFromJsonAsync<WorkspaceState>(ct);
        Assert.False(Assert.Single(closed!.Windows).IsOpen);
        using var reopenedResponse = await brain.HttpClient.PostAsJsonAsync("/workspaces/workspace-a/windows/window/reopen", new { operationId = "explicit-reopen", expectedRevision = 2 }, ct);
        Assert.Equal(HttpStatusCode.OK, reopenedResponse.StatusCode);
        var reopened = await reopenedResponse.Content.ReadFromJsonAsync<WorkspaceState>(ct);
        Assert.True(Assert.Single(reopened!.Windows).IsOpen);
    }
}