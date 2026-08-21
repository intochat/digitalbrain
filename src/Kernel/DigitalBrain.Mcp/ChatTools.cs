using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class ChatTools(IDigitalBrain brain)
{
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumTimeoutSeconds = 300;
    private static readonly PrincipalId Operator =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    [McpServerTool(Name = McpSurface.SendChatMessage)]
    [Description("Send a message to the assistant and wait for its text response.")]
    public async Task<string> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Caller-generated command id used to resume an interrupted call")]
        string commandId,
        [Description("Conversation name, for example 'main'")] string chatName = "main",
        [Description("Maximum wait in seconds, from 1 through 300")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutSeconds, MaximumTimeoutSeconds);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        var chatInstance = PrincipalPartition.InstanceName(Operator, chatName);
        var chatId = NeuronId.For<IChat>(brain.Owner, chatInstance);
        var command = new CommandId(commandIdentity);

        await brain.ActivateAsync(cancellationToken);
        await brain.GetGrainProxy<IChat>(chatInstance).Send(
            new SendMessage(command, text, new ActorContext(Operator, "operator")));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(chatId, command, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private async Task<string> WaitForResponseAsync(
        NeuronId chatId,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            cancellationToken))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is Responded response && response.CommandId == commandId)
                {
                    return response.Text;
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }
}
