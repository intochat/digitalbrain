using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;

namespace DigitalBrain.Ui;

internal static class ShellEventFeed
{
    private const string SseConnectedComment = ": connected\n\n";
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteSceneOpenedSseAsync(
        Stream responseBody,
        OwnerSessionJournal sessionJournal,
        string shellName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await WriteAsync(responseBody, SseConnectedComment, cancellationToken);

        var cursor = afterSequence;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await sessionJournal.ReadShellOutgoingAsync(shellName, cursor);
            foreach (var projected in ProjectSceneOpened(batch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteEventAsync(responseBody, projected, cancellationToken);
                cursor = Math.Max(cursor, projected.Sequence);
            }

            if (batch.ResumeSequence > cursor)
            {
                cursor = batch.ResumeSequence;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static Task WriteEventAsync(
        Stream responseBody,
        SceneOpenedEvent projected,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(projected, EventJson);
        var frame = FormattableString.Invariant(
            $"id: {projected.Sequence}\nevent: {UIEdgeContract.SceneOpenedEvent}\ndata: {payload}\n\n");
        return WriteAsync(responseBody, frame, cancellationToken);
    }

    private static async Task WriteAsync(
        Stream responseBody,
        string text,
        CancellationToken cancellationToken)
    {
        var bytes = Utf8.GetBytes(text);
        await responseBody.WriteAsync(bytes, cancellationToken);
        await responseBody.FlushAsync(cancellationToken);
    }

    private static IEnumerable<SceneOpenedEvent> ProjectSceneOpened(JournalRead batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in batch.Delta)
        {
            if (delivery.Synapse is not SceneOpened opened)
            {
                continue;
            }

            yield return new SceneOpenedEvent(
                delivery.Sequence,
                opened.SceneKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Shell.ToString());
        }
    }
}
