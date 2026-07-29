using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal static class ChatEventFeed
{
    private const string SseConnectedComment = ": connected\n\n";
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteChatTurnSseAsync(
        Stream responseBody,
        OwnerSessionJournal sessionJournal,
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await WriteAsync(responseBody, SseConnectedComment, cancellationToken);

        var cursor = afterSequence;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await sessionJournal.ReadChatOutgoingAsync(chatName, cursor);
            foreach (var turn in ProjectTurns(batch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteEventAsync(responseBody, turn, cancellationToken);
                cursor = Math.Max(cursor, turn.Sequence);
            }

            if (batch.ResumeSequence > cursor)
            {
                cursor = batch.ResumeSequence;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static IEnumerable<ChatTurnEvent> ProjectTurns(JournalRead batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in batch.Delta)
        {
            switch (delivery.Synapse)
            {
                case UserMessaged messaged:
                    yield return new ChatTurnEvent(
                        delivery.Sequence,
                        FromUser: true,
                        messaged.Text,
                        messaged.CommandId.ToString(),
                        nameof(UserMessaged),
                        messaged.Chat.ToString(),
                        delivery.Caller.ToString(),
                        delivery.CorrelationId.ToString(),
                        delivery.Timestamp);
                    break;

                case AssistantResponded responded:
                    yield return new ChatTurnEvent(
                        delivery.Sequence,
                        FromUser: false,
                        responded.Text,
                        responded.CommandId.ToString(),
                        nameof(AssistantResponded),
                        responded.Chat.ToString(),
                        delivery.Caller.ToString(),
                        delivery.CorrelationId.ToString(),
                        delivery.Timestamp);
                    break;
            }
        }
    }

    private static Task WriteEventAsync(Stream responseBody, ChatTurnEvent turn, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(turn, EventJson);
        var frame = FormattableString.Invariant($"id: {turn.Sequence}\nevent: {UiHttpContract.ChatTurnEvent}\ndata: {payload}\n\n");
        return WriteAsync(responseBody, frame, cancellationToken);
    }

    private static async Task WriteAsync(Stream responseBody, string text, CancellationToken cancellationToken)
    {
        var bytes = Utf8.GetBytes(text);
        await responseBody.WriteAsync(bytes, cancellationToken);
        await responseBody.FlushAsync(cancellationToken);
    }
}
