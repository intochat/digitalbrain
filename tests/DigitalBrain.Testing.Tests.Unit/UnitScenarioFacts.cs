using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using System.Text.Json;
using DigitalBrain.Capabilities.Http;
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
        public sealed record Watch(int Unseen = 0, string LastId = "")
        {
            public Watch() : this(0, "") { }
        }

        public static class XWatch
        {
            // The feed is newest first; LastId is the newest post already counted, so a post is counted once.
            [Synapse(typeof(Tick), "x.watch", Every = "every 6m")]
            public static async Task<Reaction<Watch>> Poll(Tick t, Watch w, [Slot] IHttpRead Http, CancellationToken ct)
            {
                var feed = await Http.GetJson("https://api.x.com/2/users/me/mentions", ct);
                var posts = feed.GetProperty("data");
                var fresh = 0;
                foreach (var post in posts.EnumerateArray())
                {
                    if (post.GetProperty("id").GetString() == w.LastId) { break; }
                    fresh++;
                }
                var lastId = fresh > 0 ? posts[0].GetProperty("id").GetString() ?? w.LastId : w.LastId;
                return Reaction<Watch>.Of(w with { Unseen = w.Unseen + fresh, LastId = lastId }, new Polled(fresh));
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

    private static string Posts(params string[] ids) =>
        JsonSerializer.Serialize(new { data = ids.Select(id => new { id, text = id }) });

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
    public async Task TwoPollsOfOnePostCountItOnce()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new Tick(Start.AddMinutes(12)), new ExpectState("x.watch", "Unseen", Json("1"))], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
        Assert.Equal([0, 1], transcript.Events.Where(e => SignalAliases.Of(e.Signal.Signal) == "x.polled").Select(e => e.Step));
    }

    [Fact]
    public async Task ASecondPostIsCountedOnce()
    {
        var calls = 0;
        var growing = new FakeSlots().Http("Http", _ => ++calls == 1
            ? """{"data":[{"id":"1","text":"a"}]}"""
            : """{"data":[{"id":"2","text":"b"},{"id":"1","text":"a"}]}""");

        var transcript = await UnitScenario.Run(Source(XWatch), growing,
            [new Tick(Start.AddMinutes(6)), new Tick(Start.AddMinutes(12)), new Tick(Start.AddMinutes(18)),
             new ExpectState("x.watch", "Unseen", Json("2")), new ExpectState("x.watch", "LastId", Json("\"2\""))], Ct);

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

    [Fact]
    public async Task ViewAndAbsenceStepsPassAndFailByName()
    {
        var passing = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new Tick(Start.AddMinutes(6)), new ExpectView("x.watch", "Unseen"), new ExpectView("x.watch", "1"), new ExpectNoSignal("x.seen")], Ct);
        Assert.True(passing.Passed, string.Join("; ", passing.Failures));

        var failing = await UnitScenario.Run(Source(XWatch), OnePost(),
            [new ExpectView("x.watch", "1"), new Tick(Start.AddMinutes(6)), new ExpectView("x.watch", "nope"), new ExpectNoSignal("x.polled")], Ct);
        Assert.Equal(
            ["step 0 ExpectView x.watch: no view published",
             "step 2 ExpectView x.watch: no text contains \"nope\"",
             "step 3 ExpectNoSignal x.polled: journaled at step 1"], failing.Failures);
    }

    [Fact]
    public async Task AScriptedWorldAnswersEachCallInOrderAndRepeatsTheLast()
    {
        var world = new World().On("Http", "GetJson", WorldAnswer.Json(Posts("1")), WorldAnswer.Json(Posts("2", "1")));
        var transcript = await UnitScenario.Run(Source(XWatch), new FakeSlots().World<IHttpRead>("Http", world),
            [new Tick(Start.AddMinutes(6)), new ExpectState("x.watch", "Unseen", Json("1")),
             new Tick(Start.AddMinutes(12)), new ExpectState("x.watch", "Unseen", Json("2")),
             new Tick(Start.AddMinutes(18)), new ExpectState("x.watch", "Unseen", Json("2"))], Ct);

        Assert.True(transcript.Passed, string.Join("; ", transcript.Failures));
        Assert.Equal(3, world.Calls.Count);
        Assert.Equal(("Http", "GetJson"), (world.Calls[0].Slot, world.Calls[0].Operation));
        Assert.Equal("https://api.x.com/2/users/me/mentions", world.Calls[0].Arguments.GetProperty("url").GetString());
    }

    [Fact]
    public async Task AFailingAnswerFailsTheStepWithWhatTheConnectionThrows()
    {
        var world = new World().On("Http", "GetJson", WorldAnswer.Status(503));
        var transcript = await UnitScenario.Run(Source(XWatch), new FakeSlots().World<IHttpRead>("Http", world), [new Tick(Start.AddMinutes(6))], Ct);

        Assert.False(transcript.Passed);
        Assert.StartsWith("step 0 Tick: HttpRequestException", Assert.Single(transcript.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANonJsonBodyThrowsWhatTheRealConnectionThrows()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), new FakeSlots().Http("Http", _ => "<html>"), [new Tick(Start.AddMinutes(6))], Ct);

        Assert.Equal("step 0 Tick: HttpRequestException: api.x.com did not return JSON", Assert.Single(transcript.Failures));
    }

    [Fact]
    public async Task AnUnscriptedOperationNamesTheSlotAndTheOperation()
    {
        var transcript = await UnitScenario.Run(Source(XWatch), new FakeSlots().World<IHttpRead>("Http", new World()), [new Tick(Start.AddMinutes(6))], Ct);

        Assert.Contains("the world has no answer for Http.GetJson", Assert.Single(transcript.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExpectedFailurePassesTheScenarioAndAnUnexpectedOneStillFailsIt()
    {
        FakeSlots Down() => new FakeSlots().World<IHttpRead>("Http", new World().On("Http", "GetJson", WorldAnswer.Status(503), WorldAnswer.Json(Posts("1"))));

        var expected = await UnitScenario.Run(Source(XWatch), Down(),
            [new Tick(Start.AddMinutes(6)), new ExpectFailure("Poll"), new Tick(Start.AddMinutes(12)), new ExpectState("x.watch", "Unseen", Json("1"))], Ct);
        Assert.True(expected.Passed, string.Join("; ", expected.Failures));

        var unexpected = await UnitScenario.Run(Source(XWatch), Down(), [new Tick(Start.AddMinutes(6)), new Tick(Start.AddMinutes(12))], Ct);
        Assert.StartsWith("step 0 Tick: HttpRequestException", Assert.Single(unexpected.Failures), StringComparison.Ordinal);

        var none = await UnitScenario.Run(Source(XWatch), OnePost(), [new Tick(Start.AddMinutes(6)), new ExpectFailure()], Ct);
        Assert.Equal("step 1 ExpectFailure: no reaction failed since the previous step", Assert.Single(none.Failures));

        var other = await UnitScenario.Run(Source(XWatch), Down(), [new Tick(Start.AddMinutes(6)), new ExpectFailure("Seen")], Ct);
        Assert.Equal(2, other.Failures.Count);
        Assert.StartsWith("step 1 ExpectFailure Seen: the functions that failed were", other.Failures[1], StringComparison.Ordinal);
    }
}
