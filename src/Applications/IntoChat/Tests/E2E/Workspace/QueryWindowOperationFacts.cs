using IntoChat.Workspace.Queries;
using Aspire.Hosting.Testing;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Supabase.Tables;
using IntoChat.Agent;
using IntoChat.Workspace;
using Npgsql;

namespace IntoChat.Tests.E2E.Workspace;

public sealed class QueryWindowOperationFacts
{
    [Fact(Timeout = 180_000)]
    public async Task ResumeAfterTableCreationUsesSameTableAndNeverReopensClosedWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var seed = new NpgsqlCommand("CREATE TABLE recovery (id int); INSERT INTO recovery VALUES (42)", connection);
        await seed.ExecuteNonQueryAsync(ct);
        var scope = WorkspaceScope.Create("owner", "recovery").Id;
        var once = true;
        var operation = new QueryWindowOperation(brain, () =>
        {
            if (once) { once = false; throw new IOException("Simulated crash after create"); }
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<IOException>(() => operation.ExecuteAsync(scope, "run", "call", "Recovery", "select id from recovery", ct));
        var result = await operation.ExecuteAsync(scope, "run", "call", "Recovery", "select id from recovery", ct);
        var workspace = brain.Get<IWorkspace>(scope);
        Assert.Equal(result.TableId, Assert.Single((await workspace.Read()).Windows).View.Id);
        var page = await brain.Get<ISupabaseTable>(result.TableId).Read(new(0, 25));
        Assert.Equal("42", Assert.Single(Assert.Single(page!.Rows).Cells));
        await workspace.Close(result.WindowId, result.WorkspaceRevision);
        Assert.Equal(result, await operation.ExecuteAsync(scope, "run", "call", "Recovery", "select id from recovery", ct));
        Assert.False(Assert.Single((await workspace.Read()).Windows).IsOpen);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation.ExecuteAsync(scope, "run", "call", "Changed", "select id from recovery", ct));
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => operation.ExecuteAsync(scope, "run", "write", "Write", "delete from recovery", ct));
        var empty = await operation.ExecuteAsync(scope, "run", "empty", "Empty", "select id from recovery where false", ct);
        Assert.Empty((await brain.Get<ISupabaseTable>(empty.TableId).Read(new(0, 25)))!.Rows);
    }
}
