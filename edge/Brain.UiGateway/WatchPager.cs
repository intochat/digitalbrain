using System.Text.Json;
using Brain.Contracts;

namespace Brain.UiGateway;

public static class WatchPager
{
    public static IReadOnlyList<string> NextFrames(NeuronEventPage page)
    {
        var frames = new List<string>();
        foreach (var evt in page.Events)
        {
            if (evt.Kind != "feed.record")
                continue;

            using var payload = JsonDocument.Parse(evt.PayloadJson);
            frames.Add(JsonSerializer.Serialize(new { sequence = evt.Revision, record = payload.RootElement.Clone() }));
        }
        return frames;
    }

    public static long NextCursor(NeuronEventPage page) => page.NextRevision;
}
