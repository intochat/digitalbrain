using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.Db;

namespace DigitalBrain.Tests.Kernel;

public class DbSupportNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task DbConnect_Fires_Input_Back()
    {
        var db = Grain<IDbSupportNeuron>("db-test-connect");
        await db.FireAsync(new DbConnect("conn1", "sqlite", "Data Source=:memory:"));

        var timeline = await db.GetTimelineAsync();
        Assert.Contains(timeline, s => s is DbConnect connect && connect.ConnectionName == "conn1" && connect.Provider == "sqlite");
    }

    [Fact]
    public async Task DbQuery_Echoes_Result()
    {
        var db = Grain<IDbSupportNeuron>("db-test-query");
        await db.FireAsync(new DbQuery("conn2", "SELECT COUNT(*) FROM items"));

        var timeline = await db.GetTimelineAsync();
        var response = timeline.OfType<DbQuery>().FirstOrDefault(q => q.ConnectionName == "conn2" && q.Result != null);
        Assert.NotNull(response);
        Assert.Contains("42 rows", response!.Result!);
        Assert.Equal("SELECT COUNT(*) FROM items", response.Query);
    }

    [Fact]
    public async Task DbInspectSchema_Fires_Schema_Result_For_Sqlite_File()
    {
        var path = await SqliteTestDatabases.CreateBudgetDatabaseAsync();
        try
        {
            var db = Grain<IDbSupportNeuron>("db-test-inspect");
            await db.FireAsync(new DbInspectSchema("budget", "sqlite", SourcePath: path, SessionId: "session-1"));

            var timeline = await db.GetTimelineAsync();
            var result = timeline.OfType<DbSchemaInspected>().LastOrDefault(s => s.ConnectionName == "budget");

            Assert.NotNull(result);
            Assert.True(result!.Succeeded);
            Assert.Null(result.Error);
            Assert.Equal("session-1", result.SessionId);
            Assert.NotNull(result.Schema);
            Assert.Contains(result.Schema!.Tables, table => table.Name == "accounts");
            Assert.Contains(result.Schema.Tables, table => table.Name == "transactions");
        }
        finally
        {
            SqliteTestDatabases.DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task DbInspectSchema_Unsupported_Provider_Fires_Clear_Failure()
    {
        var db = Grain<IDbSupportNeuron>("db-test-unsupported");
        await db.FireAsync(new DbInspectSchema("pg", "postgres", ConnectionString: "Host=localhost"));

        var timeline = await db.GetTimelineAsync();
        var result = timeline.OfType<DbSchemaInspected>().LastOrDefault(s => s.ConnectionName == "pg");

        Assert.NotNull(result);
        Assert.False(result!.Succeeded);
        Assert.Null(result.Schema);
        Assert.Contains("Unsupported database provider", result.Error);
    }
}
