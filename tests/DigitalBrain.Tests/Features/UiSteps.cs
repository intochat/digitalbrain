using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class UiSteps(BrainSteps brain, BrainWorld world)
{
    private WorkspaceRecord? _firstWorkspace;
    private WorkspaceRecord? _lastWorkspace;

    [Given("card delivery is delayed")]
    public void DelayCards() => world.CardDeliveryDelay = TimeSpan.FromSeconds(2);

    [Then(@"the latest ""(.*)"" Responded carries a rendered chart titled ""(.*)""")]
    public async Task RenderedCard(string observer, string title)
    {
        var response = await LatestResponded(observer);
        Assert.NotNull(response.Cards);
        var card = Assert.Single(response.Cards);
        Assert.Equal(UiCardKinds.Chart, card.Kind);
        Assert.Equal(title, card.Caption);
        await ChartSnapshot(card.Name, title, "Q1", 42);
    }

    [Then(@"the latest ""(.*)"" Responded carries no cards")]
    public async Task NoCards(string observer)
    {
        var response = await LatestResponded(observer);
        Assert.True(response.Cards is null or { Count: 0 });
    }

    [Then(@"the scripted model received a chart result for ""(.*)"" with point ""(.*)"" valued (\d+)")]
    public async Task ChartResult(string title, string label, int value)
    {
        var result = world.Scripted.Calls
            .SelectMany(call => call)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(content => content.Result)
            .OfType<JsonElement>()
            .Single(element => element.ValueKind == JsonValueKind.Object
                && element.GetProperty("kind").GetString() == UiCardKinds.Chart);
        Assert.Equal(title, result.GetProperty("title").GetString());
        var point = Assert.Single(result.GetProperty("points").EnumerateArray());
        Assert.Equal(label, point.GetProperty("label").GetString());
        Assert.Equal(value, point.GetProperty("value").GetInt32());
        await ChartSnapshot(result.GetProperty("id").GetString()!, title, label, value);
    }

    private async Task<Responded> LatestResponded(string observer)
    {
        var body = (await brain.Journal(observer, JournalKind.Incoming)).Delta
            .Last(delivery => delivery.Signal.Type == UIVocabulary.Responded).Signal.Body;
        var response = JsonSerializer.Deserialize(body, UIJson.Default.Responded);
        Assert.NotNull(response);
        Assert.Equal("here is your chart", response.Text);
        return response;
    }

    [When(@"chart ""(.*)"" renders ""(.*)""")]
    public async Task Render(string name, string title)
    {
        var accepted = await Chart(name).Render(new RenderChart(CommandId.New(), title, "line", [new ChartPoint("Q1", 42)]));
        Assert.Equal(name, accepted.Receipt);
        await UiWait.Until(async () => (await Chart(name).Read()).Title == title,
            $"Chart {name} did not render title '{title}' within 10 seconds.");
    }

    [When(@"chart ""(.*)"" appends point ""(.*)"" valued (\d+) with event id ""(.*)"" twice")]
    public async Task AppendTwice(string name, string label, int value, string eventId)
    {
        var chart = Chart(name);
        var point = new ChartPoint(label, value, EventId: eventId);
        for (var index = 0; index < 2; index++)
        {
            var accepted = await chart.Append(new AppendChartPoint(CommandId.New(), point, "Sales"));
            Assert.Equal(name, accepted.Receipt);
        }

        await UiWait.Until(async () => await chart.ReadPendingCount() == 0 && (await chart.Read()).Points.Count > 0);
    }

    [Then(@"chart ""(.*)"" contains ""(.*)"" with point ""(.*)"" valued (\d+)")]
    public async Task ChartSnapshot(string name, string title, string label, int value)
    {
        var state = await Chart(name).Read();
        Assert.Equal(title, state.Title);
        Assert.Equal("line", state.ChartKind);
        var point = Assert.Single(state.Points);
        Assert.Equal(label, point.Label);
        Assert.Equal(value, point.Value);
    }

    [Then(@"chat ""(.*)"" offers chart ""(.*)"" titled ""(.*)""")]
    public async Task Card(string chatName, string name, string title)
    {
        var chat = brain.Brain.Grains.GetGrain<IChat>(new NeuronId("uichat", chatName).ToGrainId());
        await UiWait.Until(async () => (await chat.ReadTurns(new ReadTurns())).Turns
            .Any(turn => turn.Cards?.Contains(new UiCardOffer("chart", name, title)) == true));
    }

    [When(@"workspace ""(.*)"" is ensured with title ""(.*)""")]
    public async Task Ensure(string name, string title)
    {
        var accepted = await Workspaces().Ensure(new EnsureWorkspace(CommandId.New(), name, title));
        _firstWorkspace ??= accepted.Receipt;
        _lastWorkspace = accepted.Receipt;
        await UiWait.Until(async () => (await Workspaces().Find(new FindWorkspace(Name: name)))?.Title == title);
    }

    [Then(@"workspace ""(.*)"" has one record titled ""(.*)"" and the same correlation")]
    public async Task Workspace(string name, string title)
    {
        Assert.NotNull(_firstWorkspace);
        Assert.NotNull(_lastWorkspace);
        var record = await Workspaces().Find(new FindWorkspace(Name: name));
        Assert.NotNull(record);
        Assert.Equal(title, record.Title);
        Assert.Equal(_firstWorkspace.CorrelationId, record.CorrelationId);
        Assert.Equal("0d6e4079e36703ebd37c00722f5891d2", record.CorrelationId);
        Assert.True(record.UpdatedAt >= _firstWorkspace.UpdatedAt);
        Assert.Equal(_lastWorkspace, record);
        Assert.Equal(record, await Workspaces().Find(new FindWorkspace(CorrelationId: record.CorrelationId)));
    }

    private IChart Chart(string name)
        => brain.Brain.Grains.GetGrain<IChart>(new NeuronId("chart", name).ToGrainId());

    private IWorkspaces Workspaces()
        => brain.Brain.Grains.GetGrain<IWorkspaces>(new NeuronId("workspaces", "default").ToGrainId());
}
