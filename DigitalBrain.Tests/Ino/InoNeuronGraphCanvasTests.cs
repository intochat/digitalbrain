using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;

namespace DigitalBrain.Tests.Ino;

public class InoNeuronGraphCanvasTests : NeuronTestBase
{
    [Fact]
    public async Task DbSchemaInspected_Emits_GraphCanvas_Surface_To_FlutterUi()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new DbSchemaInspected("budget", "sqlite", BudgetSchema(), ClientId: "session-1"));

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = Assert.Single((await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>());

        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal("assistant", surface.Props["role"]);
        Assert.Equal("session-1", surface.Props["clientId"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.GraphCanvas, tree.Type);
        Assert.Equal("E:\\budget.db schema", tree.Props["title"]);

        var nodes = Assert.IsAssignableFrom<object[]>(tree.Props["nodes"]);
        var edges = Assert.IsAssignableFrom<object[]>(tree.Props["edges"]);
        Assert.Equal(2, nodes.Length);
        Assert.Single(edges);

        var timeline = await ino.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<MemorySummary>(), summary => summary.Summary.Contains("accounts"));
    }

    [Fact]
    public async Task InoRequest_DrawRelationOfTwoObjects_Emits_GraphCanvas_Surface()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new InoRequest("draw relation of 2 objects", "session-2"));

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = Assert.Single((await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>());
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);

        Assert.Equal(UiKitVocabulary.GraphCanvas, tree.Type);
        Assert.Equal("Object relation", tree.Props["title"]);
        Assert.Equal("session-2", surface.Props["clientId"]);

        var nodes = Assert.IsAssignableFrom<object[]>(tree.Props["nodes"]);
        var edges = Assert.IsAssignableFrom<object[]>(tree.Props["edges"]);
        Assert.Equal(2, nodes.Length);
        Assert.Single(edges);
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
                new[] { new DbIndex("ix_transactions_account_id", "transactions", new[] { "account_id" }) })
        },
        @"E:\budget.db",
        "session-1");
}
