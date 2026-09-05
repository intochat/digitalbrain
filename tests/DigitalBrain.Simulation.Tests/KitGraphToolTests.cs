using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

// Mirrors ExcelKitTests: drives the AI tool directly rather than a whole chat turn, so
// the entity write and the card post are proven without depending on the model choosing
// to call the tool.
[Collection(SimulationCollection.Name)]
public sealed class KitGraphToolTests(SimulationFixture fixture)
{
    private static readonly OwnerId Owner = new(DigitalBrainNames.DefaultOwner);

    [Fact]
    public async Task ShowGraphToolCreatesTheEntityAndPostsACard()
    {
        var chatInstance = NewChatInstance();
        var show = ShowGraphTool();

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "Module deps",
            ["nodeIds"] = new[] { "brain", "excel", "sheet" },
            ["nodeLabels"] = new[] { "BRAIN", "EXCEL", "budget.xlsx" },
            ["edges"] = new[] { "brain>excel", "excel>sheet" },
        }, CancellationToken.None);

        var replyText = reply!.ToString()!;
        Assert.Contains("Module deps", replyText);

        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Contains(transcript.Turns, turn => turn.Text == "Module deps");

        var cardName = CardNameFrom(replyText);
        var instance = KitInstanceNames.Sibling(chatInstance, cardName);
        var state = await fixture.Sim.Grains.GetGrain<IGraph>(instance).Read();

        Assert.NotNull(state);
        Assert.Equal("Module deps", state!.Title);
        Assert.Equal(3, state.Nodes.Count);
        Assert.Equal(GraphNodeKinds.Hub, state.Nodes[0].Kind);
        Assert.Equal(GraphNodeKinds.Leaf, state.Nodes[1].Kind);
        Assert.Equal("budget.xlsx", state.Nodes[2].Label);
        Assert.Equal(2, state.Edges.Count);
        Assert.Equal("brain", state.Edges[0].SourceId);
        Assert.Equal("sheet", state.Edges[1].TargetId);
    }

    [Fact]
    public async Task ShowGraphToolRefusesABlankTitleWithoutTouchingTheChat()
    {
        var chatInstance = NewChatInstance();
        var show = ShowGraphTool();

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "   ",
            ["nodeIds"] = new[] { "brain" },
            ["nodeLabels"] = new[] { "BRAIN" },
            ["edges"] = Array.Empty<string>(),
        }, CancellationToken.None);

        Assert.Contains("blank", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Empty(transcript.Turns);
    }

    [Fact]
    public async Task ShowGraphToolRefusesAnEdgeNamingAnUnknownNode()
    {
        var chatInstance = NewChatInstance();
        var show = ShowGraphTool();

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "Broken",
            ["nodeIds"] = new[] { "brain" },
            ["nodeLabels"] = new[] { "BRAIN" },
            ["edges"] = new[] { "brain>ghost" },
        }, CancellationToken.None);

        Assert.Contains("brain>ghost", reply!.ToString(), StringComparison.Ordinal);
        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Empty(transcript.Turns);
    }

    [Fact]
    public async Task ShowGraphToolRefusesLabelsThatDoNotMatchTheNodeIds()
    {
        var chatInstance = NewChatInstance();
        var show = ShowGraphTool();

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = chatInstance,
            ["title"] = "Mismatched",
            ["nodeIds"] = new[] { "a", "b" },
            ["nodeLabels"] = new[] { "A" },
            ["edges"] = Array.Empty<string>(),
        }, CancellationToken.None);

        Assert.Contains("same length", reply!.ToString(), StringComparison.OrdinalIgnoreCase);
        var transcript = await ChatTranscriptRead.ForGrainKeyAsync(fixture.Sim, chatInstance, TestContext.Current.CancellationToken);
        Assert.Empty(transcript.Turns);
    }

    [Fact]
    public async Task ShowGraphToolRefusesAChatOutsideTheOwnersPartition()
    {
        var show = ShowGraphTool();

        var reply = await show.InvokeAsync(new AIFunctionArguments
        {
            ["chatName"] = "someone-else/chat",
            ["title"] = "Deps",
            ["nodeIds"] = new[] { "brain" },
            ["nodeLabels"] = new[] { "BRAIN" },
            ["edges"] = Array.Empty<string>(),
        }, CancellationToken.None);

        Assert.Contains("chat key of this owner", reply!.ToString(), StringComparison.Ordinal);
    }

    private AIFunction ShowGraphTool()
    {
        var tools = new KitToolSource(fixture.Sim.Grains, null, new MemoryKitImageStore());
        return tools.PrepareTestTools(Owner).Single(tool => tool.Name == "show_graph");
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
