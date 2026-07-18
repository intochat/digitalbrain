using System.Text.Json;
using Xunit;

namespace Ino.Domains.Travel.Tests.Storyboard;

public class StoryboardRecorderTests
{
    [Fact]
    public void RecordedEventsExportInOrder()
    {
        var recorder = new StoryboardRecorder("tokyo", "Tokyo plan · 6s");
        recorder.AppendOrb(0.0, "listening");
        recorder.AppendSynapse(
            1.2, "Cortex", "PlanTrip",
            JsonSerializer.SerializeToElement(new { intent = "plan_trip" }),
            gold: false);
        recorder.AppendCard(3.0, "flights", "enter", "travel");

        var exported = recorder.ToJson();
        using var doc = JsonDocument.Parse(exported);
        var events = doc.RootElement.GetProperty("events").EnumerateArray().ToList();

        Assert.Equal(3, events.Count);
        Assert.Equal("orb", events[0].GetProperty("kind").GetString());
        Assert.Equal("syn", events[1].GetProperty("kind").GetString());
        Assert.Equal("card", events[2].GetProperty("kind").GetString());
    }

    [Fact]
    public void GoldFlagSerializedExplicitly()
    {
        var recorder = new StoryboardRecorder("tokyo", "");
        recorder.AppendSynapse(
            2.0, "Preferences", "PlanTrip",
            JsonSerializer.SerializeToElement(new { ryokanBias = 0.62 }),
            gold: true);

        using var doc = JsonDocument.Parse(recorder.ToJson());
        var ev = doc.RootElement.GetProperty("events")[0];
        Assert.True(ev.GetProperty("gold").GetBoolean());
    }

    [Fact]
    public void RootCarriesIdLabelAndDuration()
    {
        var recorder = new StoryboardRecorder("tokyo-replan", "make day 3 cheaper");
        recorder.AppendOrb(0.10, "thinking");
        recorder.AppendOrb(1.40, "idle");

        using var doc = JsonDocument.Parse(recorder.ToJson());
        Assert.Equal("tokyo-replan", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("make day 3 cheaper", doc.RootElement.GetProperty("label").GetString());
        Assert.True(doc.RootElement.GetProperty("duration_s").GetDouble() >= 1.40);
    }

    [Fact]
    public void UtterEventSerializesText()
    {
        var recorder = new StoryboardRecorder("tokyo", "");
        recorder.AppendUtter(0.0, "Plan a 5-day Tokyo trip in late October.");
        using var doc = JsonDocument.Parse(recorder.ToJson());
        var ev = doc.RootElement.GetProperty("events")[0];
        Assert.Equal("utter", ev.GetProperty("kind").GetString());
        Assert.Equal("Plan a 5-day Tokyo trip in late October.",
            ev.GetProperty("text").GetString());
    }

    [Fact]
    public void CardEventCarriesStageAndFromCluster()
    {
        var recorder = new StoryboardRecorder("tokyo", "");
        recorder.AppendCard(3.0, "flights", "enter", "travel");
        recorder.AppendCard(1.20, "hotels", "morph", null);

        using var doc = JsonDocument.Parse(recorder.ToJson());
        var enter = doc.RootElement.GetProperty("events")[0];
        Assert.Equal("enter", enter.GetProperty("stage").GetString());
        Assert.Equal("travel", enter.GetProperty("from").GetString());

        var morph = doc.RootElement.GetProperty("events")[1];
        Assert.Equal("morph", morph.GetProperty("stage").GetString());
        Assert.True(morph.GetProperty("from").ValueKind == JsonValueKind.Null);
    }
}
