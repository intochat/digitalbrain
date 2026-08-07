using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Mcp;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class ChatTools(IDigitalBrain brain)
{
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumTimeoutSeconds = 300;

    [McpServerTool(Name = McpSurface.SendChatMessage)]
    [Description(
        "Send a message through the owner's DigitalBrain conversation and wait for the assistant "
        + "response journaled under this command id. This is the same durable chat path used by the product UI.")]
    public async Task<ChatMessageResult> SendChatMessageAsync(
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

        var chatId = NeuronId.For<IChat>(brain.Owner, chatName);
        var authorizationId = NeuronId.For<IMcpAuthorization>(brain.Owner, McpAuthorizationNeuron.InstanceName);
        var command = new CommandId(commandIdentity);

        // Activate owner brain first so DigitalBrainActivated boots capability projection
        // before the assistant turn resolves catalog tools.
        await brain.ActivateAsync(cancellationToken);
        await brain.GetGrainProxy<IChat>(chatName).Send(new SendMessage(command, text));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(chatId, authorizationId, chatName, command, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private async Task<ChatMessageResult> WaitForResponseAsync(
        NeuronId chatId,
        NeuronId authorizationId,
        string chatName,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        long authorizationCursor = 0;

        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            cancellationToken))
        {
            var authorizationPage = await brain.ReadJournalAsync(
                authorizationId,
                JournalKind.Outgoing,
                authorizationCursor,
                cancellationToken);
            ThrowIfAuthorizationRequired(authorizationPage);
            if (authorizationPage.Delta.Count > 0)
            {
                authorizationCursor = authorizationPage.Delta[^1].Sequence;
            }

            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is AssistantResponded response
                    && response.CommandId == commandId)
                {
                    return new ChatMessageResult(
                        chatName,
                        commandId.ToString(),
                        delivery.CorrelationId.Value.ToString("N"),
                        response.Text,
                        delivery.Sequence,
                        delivery.Timestamp);
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }

    private static void ThrowIfAuthorizationRequired(JournalRead page)
    {
        ArgumentNullException.ThrowIfNull(page);

        foreach (var delivery in page.Delta)
        {
            if (delivery.Synapse is AuthorizationRequired required)
            {
                throw McpAuthorizationElicitation.For(required);
            }
        }
    }
}
