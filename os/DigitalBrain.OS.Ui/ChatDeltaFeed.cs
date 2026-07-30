using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Flutter.Http;

internal static class ChatDeltaFeed
{
    internal static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);

    public static async IAsyncEnumerable<SseItem<ChatResponseUpdate>> StreamDeltasAsync(
        IDigitalBrain brain,
        string chatName,
        string text,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var turn = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        turn.CancelAfter(TurnBudget);

        var command = CommandId.New();
        await foreach (var chunk in brain.GetGrainProxy<IChat>(chatName)
            .SendStreaming(new SendMessage(command, text), turn.Token))
        {
            turn.Token.ThrowIfCancellationRequested();
            yield return new SseItem<ChatResponseUpdate>(chunk, FlutterHttpContract.ChatDeltaEvent);
        }
    }
}
