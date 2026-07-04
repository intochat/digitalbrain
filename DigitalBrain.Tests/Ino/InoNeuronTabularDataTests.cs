using System.Text.Json;
using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;

namespace DigitalBrain.Tests.Ino;

public class InoNeuronTabularDataTests : NeuronTestBase
{
    [Fact]
    public async Task TabularDataIngested_Emits_Heading_And_Table_Surface_To_FlutterUi()
    {
        var headers = new[] { "Month", "Revenue", "Units" };
        var rows = new List<List<string>>
        {
            new() { "Jan", "12000", "45" },
            new() { "Feb", "14500", "52" },
        };
        var stats = new[] { new { header = "Revenue", sum = 26500.0 } };

        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new TabularDataIngested(
            "q2-sales.xlsx",
            JsonSerializer.Serialize(headers),
            JsonSerializer.Serialize(rows),
            JsonSerializer.Serialize(stats),
            "session-1",
            "finance"));

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var timeline = await flutter.GetIncomingTimelineAsync();

        var surface = Assert.Single(timeline.OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal("session-1", surface.Props["clientId"]);
        Assert.Equal("finance", surface.Props["workspaceId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Panel, tree.Type);
        Assert.NotNull(tree.Children);
        var heading = tree.Children!.Single(c => c.Type == UiKitVocabulary.Heading);
        Assert.Equal("q2-sales.xlsx", heading.Props["text"]);
        var table = tree.Children!.Single(c => c.Type == UiKitVocabulary.Table);
        Assert.Equal(headers, table.Props["columns"]);
        Assert.Equal(rows, table.Props["rows"]);
    }

    [Fact]
    public async Task TabularDataIngested_Journals_Context_So_Followup_Question_Sees_The_Data()
    {
        var ino = Grain<IInoNeuron>("ino-main");
        await ino.FireAsync(new TabularDataIngested(
            "q2-sales.xlsx",
            JsonSerializer.Serialize(new[] { "Month", "Revenue" }),
            JsonSerializer.Serialize(new List<List<string>> { new() { "Jan", "12000" } }),
            JsonSerializer.Serialize(new object[0]),
            "session-1",
            "finance"));

        await ino.FireAsync(new InoRequest("what was the total revenue?", "session-1", "finance"));

        var timeline = await ino.GetOutgoingTimelineAsync();
        var reply = timeline.OfType<InoResponse>().LastOrDefault();
        Assert.NotNull(reply);
        Assert.False(string.IsNullOrWhiteSpace(reply!.Response));
        Assert.Contains(timeline.OfType<MemorySummary>(), m =>
            m.Summary.Contains("q2-sales.xlsx") &&
            m.WorkspaceId == "finance");
    }
}
