using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

internal static class ChatDeltaFeed
{
    internal static readonly TimeSpan TurnBudget =
        TimeSpan.Parse(NeuronCallTimeouts.LongRunning, System.Globalization.CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions AiJson = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
    };
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteChatDeltaSseAsync(
        Stream responseBody,
        IDigitalBrain brain,
        string chatName,
        string text,
        CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(responseBody);
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using var turn = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        turn.CancelAfter(TurnBudget);

        var command = CommandId.New();
        await foreach (var chunk in brain.Get<IChat>(chatName)
            .SendStreaming(new SendMessage(command, text), turn.Token))
        {
            turn.Token.ThrowIfCancellationRequested();
            await WriteDeltaAsync(responseBody, chunk, turn.Token);
        }
    }

    private static async Task WriteDeltaAsync(
        Stream responseBody,
        ChatResponseUpdate chunk,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(chunk, AiJson);
        var frame = FormattableString.Invariant(
            $"event: {UIEdgeContract.ChatDeltaEvent}\ndata: {payload}\n\n");
        var bytes = Utf8.GetBytes(frame);
        await responseBody.WriteAsync(bytes, cancellationToken);
        await responseBody.FlushAsync(cancellationToken);
    }
}
