using Ino.Core.Capabilities;
using Ino.Kernel.Contracts;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Phase 4 epilogue Slice 3A: <see cref="CortexJournal"/> per-user routing
/// decision buffer. Tests are direct-object style (grain instantiated without
/// an Orleans cluster) because CortexJournal is a pure in-memory data structure.
/// </summary>
public sealed class CortexJournalTests
{
    static RoutingDecision FakeDecision(string prompt) => new(
        Prompt: prompt,
        Source: RoutingSource.Regex,
        NeuronId: "test.exp",
        Confidence: 1.0,
        At: DateTimeOffset.UtcNow,
        MlPrediction: null,
        MlConfidence: null,
        LlmCalled: false,
        RoutingDurationMs: 1,
        CorrelationId: "corr-1");

    [Fact]
    public async Task Buffer_caps_at_20_per_user()
    {
        var journal = new CortexJournal();
        for (var i = 0; i < 25; i++)
            await journal.RecordAsync("u1", FakeDecision($"prompt-{i}"));

        var recent = await journal.GetRecentAsync("u1", 100);
        Assert.Equal(20, recent.Count);
    }

    [Fact]
    public async Task GetRecent_returns_newest_first()
    {
        var journal = new CortexJournal();
        await journal.RecordAsync("u2", FakeDecision("first"));
        await journal.RecordAsync("u2", FakeDecision("second"));
        await journal.RecordAsync("u2", FakeDecision("third"));

        var recent = await journal.GetRecentAsync("u2", 10);
        Assert.Equal(3, recent.Count);
        Assert.Equal("third", recent[0].Prompt);
        Assert.Equal("second", recent[1].Prompt);
        Assert.Equal("first", recent[2].Prompt);
    }

    [Fact]
    public async Task Multi_user_isolation()
    {
        var journal = new CortexJournal();
        await journal.RecordAsync("ua", FakeDecision("a-prompt"));
        await journal.RecordAsync("ub", FakeDecision("b-prompt"));

        var ua = await journal.GetRecentAsync("ua", 10);
        var ub = await journal.GetRecentAsync("ub", 10);

        Assert.Single(ua, d => d.Prompt == "a-prompt");
        Assert.Single(ub, d => d.Prompt == "b-prompt");
    }
}
