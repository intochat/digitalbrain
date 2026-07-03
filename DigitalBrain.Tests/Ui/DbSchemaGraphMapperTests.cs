using DigitalBrain.Core;

namespace DigitalBrain.Tests.Ui;

public class DbSchemaGraphMapperTests
{
    [Fact]
    public void ToGraphCanvasSpec_Maps_Tables_To_Nodes_And_ForeignKeys_To_Edges()
    {
        var schema = BudgetSchema();

        var spec = DbSchemaGraphMapper.ToGraphCanvasSpec(schema);

        Assert.Equal("E:\\budget.db schema", spec.Title);
        Assert.Equal("schema", spec.Layout);
        Assert.Equal(2, spec.Nodes.Count);
        Assert.Contains(spec.Nodes, node => node.Id == "accounts" && node.Label == "accounts");

        var transactions = spec.Nodes.Single(node => node.Id == "transactions");
        Assert.Contains(transactions.Fields!, field => field.Name == "account_id" && field.Badge!.Contains("FK"));

        var edge = Assert.Single(spec.Edges);
        Assert.Equal("transactions", edge.From);
        Assert.Equal("accounts", edge.To);
        Assert.Equal("account_id -> id", edge.Label);

        var tree = DbSchemaGraphMapper.ToGraphCanvasTree(schema);
        Assert.Equal(UiKitVocabulary.GraphCanvas, tree.Type);
        Assert.True(tree.Props.ContainsKey("nodes"));
        Assert.True(tree.Props.ContainsKey("edges"));
    }

    private static DbSchemaModel BudgetSchema() => new(
        "budget",
        "sqlite",
        new[]
        {
            new DbTable(
                "accounts",
                "table",
                new[]
                {
                    new DbColumn("id", "INTEGER", IsNullable: false, PrimaryKeyOrdinal: 1),
                    new DbColumn("name", "TEXT", IsNullable: false)
                },
                Array.Empty<DbForeignKey>(),
                Array.Empty<DbIndex>()),
            new DbTable(
                "transactions",
                "table",
                new[]
                {
                    new DbColumn("id", "INTEGER", IsNullable: false, PrimaryKeyOrdinal: 1),
                    new DbColumn("account_id", "INTEGER", IsNullable: false),
                    new DbColumn("amount", "REAL", IsNullable: false)
                },
                new[]
                {
                    new DbForeignKey(
                        "fk_transactions_0",
                        "transactions",
                        new[] { "account_id" },
                        "accounts",
                        new[] { "id" })
                },
                new[]
                {
                    new DbIndex("ix_transactions_account_id", "transactions", new[] { "account_id" })
                })
        },
        @"E:\budget.db",
        "session-1");
}
