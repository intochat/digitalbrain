using System.Text.Json;
using DigitalBrain.Testing.Synapses;

namespace DigitalBrain.Testing.Tests.Unit;

// The OS Creator runs a unit's scenarios before it deploys it: compile, load on a TestOS, bind slots to fakes, drive, read a transcript.
public sealed class UnitScenarioFacts
{
    // The spec's X watch: every six minutes it reads the mentions through its Http slot, counts the unseen posts and says so.
    private const string XWatch = """
        using DigitalBrain.Capabilities.Http;

        namespace Me.XWatch;

        [Signal("x.polled")]
        public sealed record Polled(int Fresh) : Signal;

        [State("x.watch")]
        public sealed record Watch(int Unseen = 0)
        {
            public Watch() : this(0) { }
        }

        public static class XWatch
        {
            [Synapse(typeof(Tick), "x.watch", Every = "every 6m")]
            public static async Task<Reaction<Watch>> Poll(Tick t, Watch w, [Slot] IHttpRead Http, CancellationToken ct)
            {
                var feed = await Http.GetJson("https://api.x.com/2/users/me/mentions", ct);
                var fresh = feed.GetProperty("data").GetArrayLength();
                return Reaction<Watch>.Of(w with { Unseen = w.Unseen + fresh }, new Polled(fresh));
            }

            [Synapse("x.seen", "x.watch")]
            public static Watch Seen(SignalEnvelope s, Watch w) => w with { Unseen = 0 };

            [Synapse("x.watch")]
            public static View Show(Watch w) => new("X", new Column([new StatCardNode("unseen", "Unseen", $"{w.Unseen}")]));
        }
        """;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SynapseSource Source(string text) =>
        new("Me.XWatch", new Dictionary<string, string> { ["XWatch.cs"] = text }, null, null);

    private static FakeSlots OnePost() => new FakeSlots().Http("Http", _ => """{"data":[{"id":"1","text":"hi"}]}""");

    private static JsonElement Json(string text) => JsonElement.Parse(text);

    [Fact]
    public async Task TheTickPollerCountsAPost()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new ExpectState("x.watch", "Unseen", Json("1")), new ExpectSignal("x.polled")], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
        Assert.Empty(transcript.Failures);
        var polled = Assert.Single(transcript.Events, e => e.Step == 0 && SignalAliases.Of(e.Signal.Signal) == "x.polled");
        Assert.Equal(1, JsonSerializer.SerializeToElement(polled.Signal.Signal, polled.Signal.Signal.GetType(), JsonSerializerOptions.Web).GetProperty("fresh").GetInt32());
        Assert.Contains(transcript.Views, v => v.Alias == "x.watch" && v.View.GetProperty("root").GetProperty("children")[0].GetProperty("value").GetString() == "1");
    }

    [Fact]
    public async Task AUnitOutsideTheBoundaryFailsBeforeItLoads()
    {
        // Cancel(bool) runs callbacks off the activation's turn: the analyzer does not flag it, the boundary does.
        var leaky = XWatch + "\npublic static class Leak { public static void Go(CancellationTokenSource s) => s.Cancel(true); }";

        var transcript = await UnitScenario.Run(Source(leaky), OnePost(), [new Tick(Start.AddMinutes(6))], Ct);

        Assert.False(transcript.Passed);
        var failure = Assert.Single(transcript.Failures);
        Assert.StartsWith("boundary: ", failure, StringComparison.Ordinal);
        Assert.Contains("System.Threading.CancellationTokenSource.Cancel", failure, StringComparison.Ordinal);
        Assert.Empty(transcript.Events);
    }

    [Fact]
    public async Task TheTranscriptListsWhatWasConsented()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(), [], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
        var sentence = Assert.Single(transcript.Consented);
        Assert.Contains("http", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("6", sentence, StringComparison.Ordinal);
        Assert.Contains("view", sentence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AConstAliasIsBound()
    {
        var text = XWatch.Replace("public static class XWatch", "public static class Names { public const string Watch = \"x.watch\"; }\n\npublic static class XWatch", StringComparison.Ordinal)
            .Replace("[State(\"x.watch\")]", "[State(Names.Watch)]", StringComparison.Ordinal);
        Assert.DoesNotContain("[State(\"", text, StringComparison.Ordinal);

        var transcript = await UnitScenario.Run(Source(text), OnePost(), [new Tick(Start.AddMinutes(6)), new ExpectState("x.watch", "Unseen", Json("1"))], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
    }

    [Fact]
    public async Task TwoTicksFireTwice()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new Tick(Start.AddMinutes(12)), new ExpectState("x.watch", "Unseen", Json("2"))], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
    }

    [Fact]
    public async Task ATickNotAfterNowFailsTheStep()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(), [new Tick(Start.AddMinutes(6)), new Tick(Start.AddMinutes(6))], Ct);

        Assert.False(transcript.Passed);
        Assert.StartsWith("step 1 Tick: At is not after now", Assert.Single(transcript.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpectStateOnANeverReactedAliasFails()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(), [new ExpectState("x.watch", "Unseen", Json("0"))], Ct);

        Assert.False(transcript.Passed);
        Assert.Equal("step 0 ExpectState: no state saved for x.watch", Assert.Single(transcript.Failures));
    }

    [Fact]
    public async Task AFailingExpectationNamesTheStep()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new ExpectState("x.watch", "Unseen", Json("2")), new ExpectSignal("x.nope")], Ct);

        Assert.False(transcript.Passed);
        Assert.Equal(2, transcript.Failures.Count);
        Assert.StartsWith("step 1 ExpectState x.watch Unseen", transcript.Failures[0], StringComparison.Ordinal);
        Assert.StartsWith("step 2 ExpectSignal x.nope", transcript.Failures[1], StringComparison.Ordinal);

        var broken = await UnitScenario.Run(Source(XWatch.Replace("w.Unseen + fresh", "w.Unseen +", StringComparison.Ordinal)), OnePost(), [], Ct);
        Assert.False(broken.Passed);
        Assert.StartsWith("compile", broken.Failures[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTranscriptCarriesOneTraceIdPerStep()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new Publish("x.seen", Json("{}")), new ExpectState("x.watch", "Unseen", Json("0"))], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
        var traces = transcript.Events.GroupBy(e => e.Step).ToDictionary(g => g.Key, g => g.Select(e => e.Signal.TraceId).Distinct().ToList());
        Assert.Equal([0, 1], traces.Keys.Order());
        Assert.All(traces.Values, ids => Assert.NotNull(Assert.Single(ids)));
        Assert.NotEqual(traces[0][0], traces[1][0]);
    }
}
