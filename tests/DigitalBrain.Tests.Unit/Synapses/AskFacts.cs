using System.Reflection;
using System.Text.Json;

namespace DigitalBrain.Tests.Unit.Synapses;

[Capability("probe", 1)]
public interface IProbe
{
    Task<JsonElement> Get(string id, CancellationToken ct);
    Task<string> Put(string id, JsonElement body, CancellationToken ct);
}

public sealed class AskFacts
{
    private static JsonElement Json(string text) => JsonElement.Parse(text);

    [Fact]
    public void AskAppendsAndKeepsStateAndSignals()
    {
        var feed = new Asks<IProbe>("feed");
        var r = Reaction<int>.Of(3, new Added(1)).Ask(feed.Call("Get", "k1", Json("{}"))).AskEach(["a", "b"], id => feed.Call("Get", id, Json("{}")));

        Assert.Equal(3, r.Next);
        Assert.Single(r.Signals);
        Assert.Equal(["k1", "a", "b"], r.Asks.Select(a => a.Key));
        Assert.All(r.Asks, a => Assert.Equal(("feed", "Get", false, (TimeSpan?)null), (a.Slot, a.Operation, a.Repeatable, a.Timeout)));
        Assert.Empty(Reaction<int>.Of(3).Asks);
    }

    [Fact]
    public void TheLimitsAreTheDesigns()
    {
        Assert.Equal((1000, 4, 2000, 2000), (AskLimits.MaxAsksPerReaction, AskLimits.Concurrency, AskLimits.MaxOpenAsks, AskLimits.MaxAsksPerHour));
        Assert.Equal((16 * 1024, 256 * 1024, 3, 25), (AskLimits.MaxArgumentsBytes, AskLimits.MaxResultBytes, AskLimits.MaxAttempts, AskLimits.AnswersPerSignal));
        Assert.Equal((TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), TimeSpan.FromHours(24)), (AskLimits.DefaultTimeout, AskLimits.MaxTimeout, AskLimits.MaxAge));
        Assert.Equal((128, 1024 * 1024, TimeSpan.FromSeconds(1)), (AskLimits.MaxKeyLength, AskLimits.MaxBatchBytes, AskLimits.AnswerFlush));
    }

    [Fact]
    public void TheKernelSignalsAreDurableWithTheirAliases()
    {
        Assert.Equal("unit.asked", typeof(Asked).GetCustomAttribute<SignalAttribute>()!.Alias);
        Assert.Equal("unit.answered", typeof(Answered).GetCustomAttribute<SignalAttribute>()!.Alias);
        Assert.False(typeof(Asked).IsDefined(typeof(EphemeralSignalAttribute), false));
        Assert.False(typeof(Answered).IsDefined(typeof(EphemeralSignalAttribute), false));
    }

    [Fact]
    public void OnlyAnOkStatusIsOk() =>
        Assert.Equal([true, false, false, false, false, false],
            Enum.GetValues<AnswerStatus>().Select(status => new Answer("s", "o", "k", status, default, null, 0).Ok));
}
