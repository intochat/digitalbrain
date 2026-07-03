using DigitalBrain.Kernel.Db;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Tests.Db;

public class SqliteSchemaInspectorTests
{
    [Fact]
    public async Task InspectFileAsync_Extracts_Tables_Columns_ForeignKeys_And_Indexes()
    {
        var path = await SqliteTestDatabases.CreateBudgetDatabaseAsync();
        try
        {
            var inspector = new SqliteSchemaInspector(NullLogger<SqliteSchemaInspector>.Instance);

            var schema = await inspector.InspectFileAsync(path, "budget", @"E:\budget.db", "session-1");

            Assert.Equal("budget", schema.ConnectionName);
            Assert.Equal("sqlite", schema.Provider);
            Assert.Equal(@"E:\budget.db", schema.SourcePath);
            Assert.Equal("session-1", schema.SessionId);
            Assert.Equal(new[] { "accounts", "transactions" }, schema.Tables.Select(t => t.Name).ToArray());

            var accounts = schema.Tables.Single(t => t.Name == "accounts");
            Assert.Equal("table", accounts.Kind);
            Assert.Contains(accounts.Columns, c => c.Name == "id" && c.PrimaryKeyOrdinal == 1 && !c.IsNullable);
            Assert.Contains(accounts.Columns, c => c.Name == "name" && c.StoreType == "TEXT" && !c.IsNullable);

            var transactions = schema.Tables.Single(t => t.Name == "transactions");
            Assert.Contains(transactions.Columns, c => c.Name == "amount" && c.StoreType == "REAL" && c.DefaultValue == "0");

            var fk = Assert.Single(transactions.ForeignKeys);
            Assert.Equal("transactions", fk.Table);
            Assert.Equal(new[] { "account_id" }, fk.Columns);
            Assert.Equal("accounts", fk.PrincipalTable);
            Assert.Equal(new[] { "id" }, fk.PrincipalColumns);
            Assert.Equal("CASCADE", fk.OnDelete);

            var index = Assert.Single(transactions.Indexes);
            Assert.Equal("ix_transactions_account_id", index.Name);
            Assert.Equal(new[] { "account_id" }, index.Columns);
            Assert.False(index.IsUnique);
        }
        finally
        {
            SqliteTestDatabases.DeleteQuietly(path);
        }
    }
}
