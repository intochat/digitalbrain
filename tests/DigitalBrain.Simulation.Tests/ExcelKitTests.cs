using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.Excel;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Collection(SimulationCollection.Name)]
public sealed class ExcelKitTests(SimulationFixture fixture)
{
    private static readonly OwnerId Owner = new(DigitalBrainNames.DefaultOwner);

    [Fact]
    public async Task SpreadsheetWrittenThroughTheToolGrainKeyIsReadableThroughTheBrainClient()
    {
        var principal = new PrincipalId(Guid.NewGuid());
        var chat = $"{fixture.Sim.Brain.Owner.Value}/"
            + PrincipalPartition.InstanceName(principal, fixture.Sim.UniqueId("chat"));
        var cardName = fixture.Sim.UniqueId("sheet");

        var toolInstance = ExcelKitNames.Sibling(chat, cardName);
        var written = new ExcelState(
            "Yesterday",
            "Sheet1",
            ["Item", "Qty"],
            [new ExcelRow(["Shoes", "2"])]);
        await fixture.Sim.Grains.GetGrain<IExcel>(toolInstance).Load(written);

        var endpointInstance = PrincipalPartition.InstanceName(principal, cardName);
        var read = await fixture.Sim.Brain.GetEntity<IExcel>(endpointInstance).Read();

        Assert.NotNull(read);
        Assert.Equal("Yesterday", read!.Title);
        Assert.Equal("Shoes", read.Rows[0].Cells[0]);
    }

    [Fact]
    public async Task ShowSpreadsheetToolCreatesTheEntityAndPostsACard()
    {
        var chatInstance = NewChatInstance();
        var tools = new ExcelToolSource(fixture.Sim.Grains);
        var show = tools.PrepareTestTools(Owner).Single(tool => tool.Name == "show_spreadsheet");

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "Yesterday",
            ["sheetName"] = "Sheet1",
            ["headers"] = "Item,Qty",
            ["rows"] = "Shoes,2",
        }, CancellationToken.None);

        var replyText = reply!.ToString()!;
        Assert.Contains("Yesterday", replyText);

        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Contains(transcript.Turns, turn => turn.Text == "Yesterday");

        var cardName = CardNameFrom(replyText);
        var instance = ExcelKitNames.Sibling(chatInstance, cardName);
        var state = await fixture.Sim.Grains.GetGrain<IExcel>(instance).Read();
        Assert.NotNull(state);
        Assert.Equal(["Item", "Qty"], state!.Columns);
        Assert.Equal("Shoes", state.Rows[0].Cells[0]);
    }

    [Fact]
    public async Task ShowSpreadsheetToolRefusesABlankTitleWithoutTouchingTheChat()
    {
        var chatInstance = NewChatInstance();
        var tools = new ExcelToolSource(fixture.Sim.Grains);
        var show = tools.PrepareTestTools(Owner).Single(tool => tool.Name == "show_spreadsheet");

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "   ",
            ["sheetName"] = "Sheet1",
            ["headers"] = "Item",
            ["rows"] = "Shoes",
        }, CancellationToken.None);

        Assert.Contains("blank", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Empty(transcript.Turns);
    }

    [Fact]
    public async Task SetCellExpandsTheGrid()
    {
        var principal = new PrincipalId(Guid.NewGuid());
        var name = PrincipalPartition.InstanceName(principal, fixture.Sim.UniqueId("sheet"));
        var excel = fixture.Sim.Brain.GetEntity<IExcel>(name);

        await excel.Load(new ExcelState("Grid", "Sheet1", ["A"], [new ExcelRow(["1"])]));
        await excel.SetCell(1, 1, "x");

        var read = await excel.Read();
        Assert.Equal(2, read!.Columns.Count);
        Assert.Equal("x", read.Rows[1].Cells[1]);
    }

    private string NewChatInstance()
        => $"{fixture.Sim.Brain.Owner.Value}/"
            + PrincipalPartition.InstanceName(new PrincipalId(Guid.NewGuid()), fixture.Sim.UniqueId("chat"));

    private static string CardNameFrom(string replyText)
    {
        const string Marker = "card '";
        var markerIndex = replyText.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Reply did not name a card: {replyText}");
        var start = markerIndex + Marker.Length;
        var end = replyText.IndexOf('\'', start);
        Assert.True(end > start, $"Reply's card name was not quote-terminated: {replyText}");
        return replyText[start..end];
    }
}
