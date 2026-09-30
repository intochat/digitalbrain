using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using Xunit;

namespace DigitalBrain.Assistant.Tests.Unit;

// Drives the same individual controls a generic renderer exposes; no chat facade.
internal sealed class AssistantUiProbe(IGrainFactory grains, string key)
{
    public IAssistant App => grains.GetGrain<IAssistant>(key);
    public ITextField Draft => grains.GetGrain<ITextField>(UiComposer.NameOf(key, AssistantSurface.DraftPart));
    public IButton Button(string part) => grains.GetGrain<IButton>(UiComposer.NameOf(key, part));
    public ISelect Select(string part) => grains.GetGrain<ISelect>(UiComposer.NameOf(key, part));

    public async Task<AssistantThread> Read()
    {
        var state = await App.Read();
        return Assert.Single(state.Threads, thread => thread.Id == state.SelectedThread);
    }

    public async Task Input(string text)
    {
        await Draft.Input(text);
        await WaitForDraft(text);
    }

    public async Task WaitForDraft(string text)
    {
        await Until(thread => thread.Draft == text);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while ((await Draft.Read()).Value != text)
        {
            Assert.True(DateTime.UtcNow < deadline, "The input projection did not settle.");
            await Task.Delay(20);
        }
    }

    public async Task Submit(string? text = null)
    {
        if (text is not null) { await Input(text); }
        await Button(AssistantSurface.SendPart).Click();
    }

    public async Task<AssistantThread> Until(Func<AssistantThread, bool> matches, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        AssistantThread state;
        while (!matches(state = await Read()))
        {
            Assert.True(DateTime.UtcNow < deadline, "Assistant did not settle: " + System.Text.Json.JsonSerializer.Serialize(state));
            await Task.Delay(20, ct);
        }
        return state;
    }

    public async Task<IReadOnlyList<UiChildRef>> Tree()
    {
        var nodes = new List<UiChildRef>();
        async Task Visit(UiChildRef node)
        {
            Assert.DoesNotContain(nodes, previous => previous.Name == node.Name);
            nodes.Add(node);
            IReadOnlyList<UiChildRef> children = node.Kind switch
            {
                "surface" => (await grains.GetGrain<ISurface>(node.Name).Read()).Definition.Children,
                "layout" => (await grains.GetGrain<ILayout>(node.Name).Read()).Definition.Children,
                "card" => (await grains.GetGrain<ICard>(node.Name).Read()).Children,
                _ => [],
            };
            foreach (var child in children) { await Visit(child); }
        }
        await Visit(new("surface", UiComposer.NameOf(key, "surface")));
        return nodes;
    }

    public async Task AssertExplicitControls()
    {
        var tree = await Tree();
        Assert.DoesNotContain(tree, node => node.Kind == "uichat");
        foreach (var (part, kind) in new[]
        {
            (AssistantSurface.ThreadsPart, "select"), (AssistantSurface.ModelPart, "select"),
            (AssistantSurface.DraftPart, "textfield"), (AssistantSurface.VoicePart, "voiceinput"),
            (AssistantSurface.AttachPart, "fileinput"), (AssistantSurface.SendPart, "button"),
            (AssistantSurface.StopPart, "button"), (AssistantSurface.NewPart, "button"),
            (AssistantSurface.StatusPart, "text"), (AssistantSurface.MessagesPart, "layout"),
            (AssistantSurface.ResultsPart, "layout"), (AssistantSurface.ReceiptsPart, "layout"),
        })
        { Assert.Contains(tree, node => node.Kind == kind && node.Name == UiComposer.NameOf(key, part)); }
        var draft = await Draft.Read();
        Assert.Equal("multiline", draft.Kind);
        Assert.Equal(UiComposer.NameOf(key, AssistantSurface.SendPart), draft.SubmitButton);
    }

    public async Task<string> VisibleText()
    {
        var text = new List<string>();
        foreach (var node in await Tree())
        {
            if (node.Kind == "text") { text.Add((await grains.GetGrain<IText>(node.Name).Read()).Markdown); }
            if (node.Kind == "card")
            {
                var card = await grains.GetGrain<ICard>(node.Name).Read();
                text.Add(card.Title); text.Add(card.Body);
            }
        }
        return string.Join("\n", text);
    }
}
