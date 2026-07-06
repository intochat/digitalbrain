using DigitalBrain.Core;

namespace DigitalBrain.Tests.Core;

public class DbSchemaContractTests
{
    [Fact]
    public void DbSchemaModel_Carries_Provider_Neutral_Schema_Metadata()
    {
        var schema = new DbSchemaModel(
            "budget",
            "sqlite",
            new[]
            {
                new DbTable(
                    "accounts",
                    "table",
                    new[] { new DbColumn("id", "INTEGER", IsNullable: false, PrimaryKeyOrdinal: 1) },
                    Array.Empty<DbForeignKey>(),
                    Array.Empty<DbIndex>())
            },
            SourcePath: @"E:\budget.db",
            SessionId: "session-1",
            Metadata: new Dictionary<string, string?> { ["sqlite:version"] = "3.46.0" },
            WorkspaceId: "finance");

        var inspected = new DbSchemaInspected("budget", "sqlite", schema, ClientId: "session-1", WorkspaceId: "finance");

        Assert.True(inspected.Succeeded);
        Assert.Equal("sqlite", inspected.Provider);
        Assert.Equal(@"E:\budget.db", inspected.Schema!.SourcePath);
        Assert.Equal("session-1", inspected.Schema.SessionId);
        Assert.Equal("finance", inspected.WorkspaceId);
        Assert.Equal("finance", inspected.Schema.WorkspaceId);
        Assert.Equal("accounts", inspected.Schema.Tables.Single().Name);
    }
}
