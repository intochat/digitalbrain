using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.UI;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
    [Description("Send a message to the assistant. If a login action is required, complete it in the browser and retry with the same text, commandId and chatName to retrieve the resumed response. Never send credentials in chat.")]
    public async Task<CallToolResult> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Caller-generated command id used to resume an interrupted call")]
        string commandId,
        [Description("Conversation name, for example 'main'")] string chatName = "main",
        [Description("Maximum wait in seconds, from 1 through 300")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default,
        McpServer? server = null)
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
            return await WaitForResponseAsync(chatId, command, server, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private async Task<CallToolResult> WaitForResponseAsync(
        NeuronId chatId,
        CommandId commandId,
        McpServer? server,
        CancellationToken cancellationToken)
    {
        var chat = brain.GetGrainProxy<IChat>(chatId.Name);
        var snapshot = await ReadTurnAsync(chat, commandId, cancellationToken);
        if (ResultFromSnapshot(snapshot, server) is { } current)
        {
            return current;
        }

        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0,
            cancellationToken))
        {
            // Journals contain every attempt, including an older login prompt. The
            // durable current status decides whether any of those events is relevant.
            snapshot = await ReadTurnAsync(chat, commandId, cancellationToken);
            if (ResultFromSnapshot(snapshot, server) is { } result)
            {
                return result;
            }

            foreach (var delivery in page.Delta)
            {
                // Compatibility for retained turns completed before answers were
                // included in snapshots. Never replay a pending-action response.
                if (snapshot.Status == ChatTurnStatus.Completed
                    && delivery.Synapse is Responded { UserAction: null } response
                    && response.CommandId == commandId)
                {
                    return TextResult(response.Text);
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }

    private static async Task<ChatTurnSnapshot> ReadTurnAsync(
        IChat chat, CommandId commandId, CancellationToken cancellationToken)
    {
        var turns = await chat.ReadTurns().WaitAsync(cancellationToken);
        return turns.FirstOrDefault(turn => turn.CommandId == commandId)
            ?? throw new InvalidOperationException("The requested chat turn is no longer retained.");
    }

    private static CallToolResult? ResultFromSnapshot(ChatTurnSnapshot turn, McpServer? server)
    {
        if (turn.Status == ChatTurnStatus.WaitingForUser && turn.UserAction is { } action)
        {
            if (server?.ClientCapabilities?.Elicitation?.Url is not null)
            {
                // Release the tool call while the user authorizes in a browser.
                // The client completes this URL and retries the same command id.
                throw new UrlElicitationRequiredException(action.Message,
                [
                    new ElicitRequestParams
                    {
                        Mode = "url",
                        ElicitationId = action.Id,
                        Url = action.LoginUrl,
                        Message = action.Message,
                    },
                ]);
            }

            return new CallToolResult
            {
                Content = [new TextContentBlock
                {
                    Text = $"{action.Message}\n\n[Log in to {action.DisplayName}]({action.LoginUrl})\n\n"
                        + "After authorizing, repeat send_chat_message with the same text, commandId and chatName. Do not paste credentials into chat.",
                }],
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    status = nameof(ChatTurnStatus.WaitingForUser),
                    commandId = turn.CommandId.ToString(),
                    turnId = turn.TurnId.ToString(),
                    userAction = action,
                }, JsonSerializerOptions.Web),
            };
        }

        if (turn.Status == ChatTurnStatus.Completed && turn.Answer is { } answer)
        {
            return TextResult(answer);
        }

        if (turn.Status is ChatTurnStatus.Failed or ChatTurnStatus.Cancelled)
        {
            var message = turn.Status == ChatTurnStatus.Cancelled
                ? "This request was cancelled. Send a new command to try again."
                : turn.Detail ?? "This request failed. Send a new command to try again.";
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = message }],
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    status = turn.Status.ToString(),
                    commandId = turn.CommandId.ToString(),
                    turnId = turn.TurnId.ToString(),
                }),
            };
        }

        return null;
    }

    private static CallToolResult TextResult(string text)
        => new() { Content = [new TextContentBlock { Text = text }] };
}
