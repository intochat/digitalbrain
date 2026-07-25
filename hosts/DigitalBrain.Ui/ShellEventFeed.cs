using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using Orleans;

namespace DigitalBrain.Ui;

internal static class ShellEventFeed
{
    private const string SessionName = "session";
    private const string SseConnectedComment = ": connected\n\n";
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteSceneOpenedSseAsync(
        Stream responseBody,
        IGrainFactory grains,
        IDigitalBrain brain,
        string shellName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var shellId = NeuronId.For<IShell>(brain.Owner, shellName);
        var session = grains.GetGrain<ISessionNeuron>(
            new NeuronId(ISessionNeuron.GrainTypeName, brain.Owner, SessionName).ToGrainId());

        await WriteAsync(responseBody, SseConnectedComment, cancellationToken);

        var cursor = afterSequence;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await session.ReadNeuronJournal(
                shellId,
                JournalKind.Outgoing,
                cursor);
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
            $"id: {projected.Sequence}\nevent: scene-opened\ndata: {payload}\n\n");
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

internal sealed record SceneOpenedEvent(
    long Sequence,
    string SceneKey,
    string Title,
    string CommandId,
    string Shell);
