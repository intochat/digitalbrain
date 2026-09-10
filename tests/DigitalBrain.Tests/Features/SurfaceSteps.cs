using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.UI;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class SurfaceSteps(BrainSteps brain)
{
    private OpenSurface? _open;
    private Accepted<SurfaceOpenReceipt>? _accepted;
    private Accepted<SurfaceOpenReceipt>? _repeated;
    private ActivityExecutionChanged? _fact;

    [When(@"surface ""(.*)"" opens ""(.*)"" titled ""(.*)"" with button ""(.*)""")]
    public async Task Open(string name, string key, string title, string button)
    {
        var commandId = _open is null ? new CommandId(Guid.Parse("11111111-1111-1111-1111-111111111111")) : CommandId.New();
        _open = new OpenSurface(commandId, key, title,
            new SurfaceComponent("column", "root", Children:
                [new SurfaceComponent("button", button, JsonSerializer.Deserialize<JsonElement>("{\"label\":\"Continue\"}"))]));
        _accepted = await Surface(name).Open(_open);
    }

    [When(@"surface ""(.*)"" retries its open command")]
    public async Task Retry(string name)
    {
        Assert.NotNull(_open);
        _repeated = await Surface(name).Open(_open);
    }

    [Then("the repeated surface receipt and work are unchanged")]
    public void Repeated()
    {
        Assert.NotNull(_accepted);
        Assert.NotNull(_repeated);
        Assert.Equal(_accepted.Work, _repeated.Work);
        Assert.Equal(JsonSerializer.Serialize(_accepted.Receipt, UIJson.Default.SurfaceOpenReceipt),
            JsonSerializer.Serialize(_repeated.Receipt, UIJson.Default.SurfaceOpenReceipt));
    }

    [Then(@"surface ""(.*)"" contains the opened scene and receipt")]
    public async Task Scene(string name)
    {
        Assert.NotNull(_open);
        Assert.NotNull(_accepted);
        var state = await Surface(name).Read();
        var scene = Assert.Single(state.Scenes);
        Assert.Equal(_open.SurfaceKey, scene.SurfaceKey);
        Assert.Equal(_open.Title, scene.Title);
        Assert.Equal("Continue", scene.Root!.Children![0].Properties!.Value.GetProperty("label").GetString());
        Assert.Contains(state.OpenReceipts!, receipt => receipt.CommandId == _open.Id && receipt.Fingerprint == _accepted.Receipt.Fingerprint);
        Assert.Matches("^[0-9A-F]{64}$", _accepted.Receipt.Fingerprint);
    }

    [Then(@"the surface receipt lists components ""(.*)""")]
    public void Additions(string keys)
    {
        Assert.NotNull(_accepted);
        Assert.Equal(keys, string.Join(", ", _accepted.Receipt.AddedComponents.Select(component => component.Key)));
    }

    [Then(@"""(.*)"" received the added components with stable event ids")]
    public async Task ComponentEvents(string session)
    {
        Assert.NotNull(_open);
        var events = (await brain.Journal(session, JournalKind.Incoming)).Delta
            .Where(item => item.Signal.Type == UIVocabulary.ComponentAdded)
            .Select(item => JsonSerializer.Deserialize(item.Signal.Body, UIJson.Default.ComponentAdded)!).ToList();
        Assert.Equal(["root", "go"], events.Select(item => item.Component.Key));
        Assert.Equal(["313e3174-2f39-3f8e-cfd1-91ce29a6f036", "310788fa-5633-61c8-70ad-74de80cb440c"],
            events.Select(item => item.EventId));
        Assert.NotNull(_accepted);
        Assert.Equal("5BFE7B147907E88956C837A6937510A95562F9EF98D0803D1E9AF1FB6BEADA4F", _accepted.Receipt.Fingerprint);
        Assert.All(events, item =>
        {
            Assert.Equal(_open.Id, item.CommandId);
            Assert.Equal(new NeuronId("surface", "desk"), item.Surface);
            Assert.Equal("home", item.SurfaceKey);
            Assert.True(Guid.TryParse(item.EventId, out _));
        });
        Assert.Equal(2, events.Select(item => item.EventId).Distinct(StringComparer.Ordinal).Count());
    }

    [When(@"surface ""(.*)"" activates ""(.*)"" on ""(.*)"" with intent ""(.*)""")]
    public async Task Activate(string name, string control, string key, string intent)
    {
        try
        {
            var accepted = await Surface(name).Activate(new ActivateControl(CommandId.New(), key, control, intent));
            Assert.Equal(new ControlActivation(key, control, intent), accepted.Receipt);
        }
        catch (ArgumentException error)
        {
            brain.RecordError(error);
        }
    }

    [When(@"an activity execution fact is fired at ""(.*)""")]
    public async Task ActivityFact(string target)
    {
        _fact = new ActivityExecutionChanged(CorrelationId.New(), "operation", SignalId.New(), null,
            NeuronId.Plain("alice"), new NeuronId("agent", "desk"), "Ask", "running", DateTimeOffset.UtcNow, "Answer question");
        await brain.FireCore("alice", UIVocabulary.ActivityExecutionChanged,
            JsonSerializer.Serialize(_fact, UIJson.Default.ActivityExecutionChanged), target);
        Assert.Null(brain.LastError);
    }

    [Then(@"surface ""(.*)"" and activities ""(.*)"" show the running activity")]
    public async Task Activity(string surface, string activities)
    {
        Assert.NotNull(_fact);
        var ledger = brain.Brain.Grains.GetGrain<IActivities>(new NeuronId("activities", activities).ToGrainId());
        var view = Assert.Single((await ledger.Read(new ReadActivities())).Activities);
        Assert.Equal(_fact.CorrelationId.ToString(), view.CorrelationId);
        Assert.Equal("running", view.Status);
        Assert.Equal("Answer question", view.Title);
        Assert.Equal(1, view.Version);
        Assert.Single(view.Events);
        await UiWait.Until(async () => (await Surface(surface).Read()).Activities?.Any(item => item.Id == view.Id) == true);
        var projected = Assert.Single((await Surface(surface).Read()).Activities!);
        Assert.Equal(JsonSerializer.Serialize(view, UIJson.Default.ActivityView), JsonSerializer.Serialize(projected, UIJson.Default.ActivityView));
    }

    private ISurface Surface(string name)
        => brain.Brain.Grains.GetGrain<ISurface>(new NeuronId("surface", name).ToGrainId());
}
