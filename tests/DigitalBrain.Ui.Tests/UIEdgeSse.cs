using System.Text.Json;

namespace DigitalBrain.Ui.Tests;

internal static class UIEdgeSse
{
    private static readonly JsonSerializerOptions EventJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string OpenScene(string shellName) =>
        UIEdgeContract.OpenScenePath.Replace("{shellName}", shellName, StringComparison.Ordinal);

    public static string ShellEvents(string shellName, long afterSequence = 0) =>
        $"{UIEdgeContract.ShellEventsPath.Replace("{shellName}", shellName, StringComparison.Ordinal)}?{UIEdgeContract.AfterSequenceQuery}={afterSequence}";

    public static string ActivateControl(string sceneKey, string controlId) =>
        UIEdgeContract.ActivateControlPath
            .Replace("{sceneKey}", sceneKey, StringComparison.Ordinal)
            .Replace("{controlId}", controlId, StringComparison.Ordinal);

    public static async Task<SceneOpenedEvent> ReadNextSceneOpenedAsync(
        StreamReader reader,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var payload = await ReadNextSceneOpenedPayloadAsync(reader, cancellationToken, timeout);
        var projected = JsonSerializer.Deserialize<SceneOpenedEvent>(payload, EventJsonOptions);
        if (projected is null
            || string.IsNullOrWhiteSpace(projected.SceneKey)
            || projected.Sequence <= 0)
        {
            throw new InvalidOperationException(
                "SSE scene-opened payload did not deserialize to a valid SceneOpenedEvent.");
        }

        return projected;
    }

    public static async Task<string> ReadNextSceneOpenedPayloadAsync(
        StreamReader reader,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));

        string? dataLine = null;
        string? eventName = null;
        while (!linked.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(linked.Token);
            if (line is null)
            {
                break;
            }

            if (line.StartsWith(':', StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLine = line["data:".Length..].Trim();
                continue;
            }

            if (line.Length == 0 && dataLine is not null)
            {
                var name = eventName;
                var payload = dataLine;
                eventName = null;
                dataLine = null;

                if (name is not null
                    && !string.Equals(name, UIEdgeContract.SceneOpenedEvent, StringComparison.Ordinal))
                {
                    continue;
                }

                if (payload.Contains("\"sceneKey\"", StringComparison.Ordinal)
                    && payload.Contains("\"sequence\"", StringComparison.Ordinal))
                {
                    return payload;
                }
            }
        }

        throw new TimeoutException(
            "SSE stream ended before a SceneOpened projection arrived — brain may have moved while UI projection is dead.");
    }
}
