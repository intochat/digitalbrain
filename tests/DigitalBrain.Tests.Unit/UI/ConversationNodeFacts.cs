using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.UI;

public sealed class ConversationNodeFacts
{
    private static readonly JsonSerializerOptions Json = RuntimeProtocol.OptionsFor(typeof(View));

    private static readonly Conversation Sample = new(
        "conv",
        [
            new MessageNode("u1", "user", "hello"),
            new MessageNode(
                "a1",
                "assistant",
                "Hi.",
                Cards: [new ApprovalCardNode("approve-1", "p-1", "Delete x.txt?", "Removes x.txt", []) { Resolve = new Resolve("p-1") }],
                Activities: [new ActivityNode("act-1", "Searched files", "done", "3 files")],
                Status: "completed",
                Model: "gpt-5.4",
                Retry: new Button("retry-u1", "Try again", new Emit("app.ino", "ino.asked", JsonSerializer.SerializeToElement(new { value = "hello" })))),
        ],
        new ComposerNode("composer", new Emit("app.ino", "ino.asked"), "Ask Ino", Enabled: true),
        Waiting: new Progress("Thinking", 0.5),
        Status: "working",
        Error: new ErrorLineNode("Ino could not answer.", "Timed out.", new Button("err-retry", "Try again", new Emit("app.ino", "ino.asked"))),
        Stop: new Button("stop", "Stop", new Emit("app.ino", "ino.cancelled")),
        Empty: new EmptyStateNode("Start a conversation", "Ask anything.", [new Button("s1", "Summarise today", new Emit("app.ino", "ino.asked"))]));

    [Fact]
    public void ConversationRoundTripsThroughViewJson()
    {
        var view = new View("Ino", Sample);

        var wire = JsonSerializer.SerializeToElement(view, Json);
        var back = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);

        Assert.Equal("conversation", wire.GetProperty("root").GetProperty("kind").GetString());
        Assert.Equal(view, back);
    }

    [Fact]
    public void ViewNodesFindsTheComposerAndEveryNestedButtonAndApprovalCard()
    {
        var wire = JsonSerializer.SerializeToElement(new View("Ino", Sample), Json);

        var composer = ViewNodes.Find(wire, "composer");
        Assert.NotNull(composer);
        Assert.Equal(("app.ino", "ino.asked", null), ViewNodes.EmitOf(composer.Value));
        Assert.Equal("hello", ViewNodes.Payload(null, "hello", "ada").GetProperty("value").GetString());

        Assert.NotNull(ViewNodes.Find(wire, "stop"));
        Assert.NotNull(ViewNodes.Find(wire, "retry-u1"));
        Assert.NotNull(ViewNodes.Find(wire, "err-retry"));
        Assert.NotNull(ViewNodes.Find(wire, "s1"));
        Assert.NotNull(ViewNodes.Find(wire, "approve-1"));
    }

    [Fact]
    public void APresentedConversationExposesItsTexts()
    {
        var wire = JsonSerializer.SerializeToElement(new View("Ino", Sample), Json);
        var presented = PresentedView.From(new PublishedView("app.ino", 1, wire));

        Assert.Contains("hello", presented.Texts);
        Assert.Contains("Hi.", presented.Texts);
        Assert.Contains("Ino could not answer.", presented.Texts);
        Assert.Contains("Try again", presented.Texts);
        Assert.Contains("Stop", presented.Texts);
    }

    [Fact]
    public void MetricTileWithSparklineAndActionsRoundTripsThroughViewJson()
    {
        var tile = Ui.MetricTile(
            "btc-tile",
            "app.btc-alert",
            "BTC / USD",
            "$98,420",
            delta: "+2.4%",
            deltaTone: "positive",
            statusLabel: "Live",
            statusTone: "accent",
            sparkline: [96100.0, 97250.5, 98420.0],
            threshold: 95000.0,
            actions: [new Button("refresh-btc", "Refresh", new Emit("app.btc-alert", "btc.refresh"))]);

        var view = new View("Dashboard", tile);
        var wire = JsonSerializer.SerializeToElement(view, Json);
        var back = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);

        Assert.Equal("metricTile", wire.GetProperty("root").GetProperty("kind").GetString());
        Assert.Equal(view, back);
        Assert.NotNull(ViewNodes.Find(wire, "refresh-btc"));

        var presented = PresentedView.From(new PublishedView("app.btc-alert", 1, wire));
        Assert.Contains("BTC / USD", presented.Texts);
        Assert.Contains("$98,420", presented.Texts);
        Assert.Contains("Refresh", presented.Texts);
    }

    [Fact]
    public void DiffCardRoundTripsThroughViewJson()
    {
        var diff = Ui.DiffCard(
            "pr-42",
            "intochat/os",
            "main",
            "a1b2c3d",
            filesChanged: 1,
            additions: 3,
            deletions: 1,
            files:
            [
                new DiffFileNode(
                    "src/App.cs",
                    "modified",
                    Additions: 3,
                    Deletions: 1,
                    Hunks: [new DiffHunkNode("@@ -1,2 +1,4 @@", ["-old", "+new1", "+new2", "+new3"])]),
            ]);

        var view = new View("Feature to PR", diff);
        var wire = JsonSerializer.SerializeToElement(view, Json);
        var back = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);

        Assert.Equal("diffCard", wire.GetProperty("root").GetProperty("kind").GetString());
        Assert.Equal(view, back);

        var presented = PresentedView.From(new PublishedView("app.f2pr", 1, wire));
        Assert.Contains("intochat/os", presented.Texts);
        Assert.Contains("src/App.cs", presented.Texts);
    }

    [Fact]
    public void ComposerNodeDefaultsDictationFalseAndRoundTripsThroughJson()
    {
        Assert.False(Sample.Composer.Dictation);
        var defaultWire = JsonSerializer.SerializeToElement(Sample.Composer, Json);
        Assert.True(defaultWire.TryGetProperty("dictation", out var defaultProp));
        Assert.False(defaultProp.GetBoolean());

        var dictated = Sample with { Composer = Sample.Composer with { Dictation = true } };
        var wire = JsonSerializer.SerializeToElement(new View("Ino", dictated), Json);
        var back = JsonSerializer.Deserialize<View>(wire.GetRawText(), Json);

        Assert.NotNull(back);
        var backConversation = Assert.IsType<Conversation>(back.Root);
        Assert.True(backConversation.Composer.Dictation);
        Assert.True(wire.GetProperty("root").GetProperty("composer").GetProperty("dictation").GetBoolean());
    }
}
