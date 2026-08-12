using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Conversations;
using DigitalBrain.Core;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace DigitalBrain.Conversations.Mcp;

// Seam 5 done bar #5: Conversations-exported MCP tools (thin host WithTools).
// Tip Chat still owns durable Responded journal until Chat dissolves into Conversations.
[McpServerToolType]
public sealed class ConversationTools(IDigitalBrain brain, IHttpContextAccessor httpContextAccessor)
{
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumTimeoutSeconds = 300;

    [McpServerTool(Name = ConversationMcpSurface.SendChatMessage)]
    [Description(
        "Send a message through the authenticated caller's principal-partitioned conversation "
        + "and wait for the assistant response.")]
    public async Task<ChatMessageResult> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Caller-generated command id used to resume an interrupted call")]
        string commandId,
        [Description("Conversation local name, for example 'main'")] string chatName = "main",
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

        var actor = McpActor.Require(httpContextAccessor);
        var conversationInstance = McpActor.Partition(actor, chatName);
        // Strangle: durable Responded still journals on tip IChat under the same instance name.
        var chatJournalId = NeuronId.For<IChat>(brain.Owner, conversationInstance);
        var authorizationId = NeuronId.For<IMcpAuthorization>(
            brain.Owner,
            IMcpAuthorization.DefaultInstanceName);
        var command = new CommandId(commandIdentity);

        await brain.ActivateAsync(cancellationToken);
        using (VerifiedActor.Enter(actor))
        {
            await brain.GetGrainProxy<IConversation>(conversationInstance)
                .Send(new SendConversationMessage(command, text, actor));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(chatJournalId, authorizationId, chatName, command, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' for principal '{actor.PrincipalId.Value:N}' within {timeoutSeconds} seconds.");
        }
    }

    [McpServerTool(Name = ConversationMcpSurface.ActivateChatButton)]
    [Description(
        "Activate a button previously offered on an assistant chat turn. "
        + "Uses the same durable ButtonClicked path as the product UI command bus.")]
    public async Task<ChatMessageResult> ActivateChatButtonAsync(
        [Description("Conversation local name, for example 'main'")] string chatName,
        [Description("Command id of the assistant turn that offered the button")] string offerCommandId,
        [Description("Button id from the offer")] string buttonId,
        [Description("Action from the offer, for example a sign-in URL or command name")] string action,
        [Description("Maximum wait in seconds, from 1 through 300")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentException.ThrowIfNullOrWhiteSpace(offerCommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutSeconds, MaximumTimeoutSeconds);

        if (!Guid.TryParse(offerCommandId, out var offerId) || offerId == Guid.Empty)
        {
            throw new ArgumentException("The offer command id must be a non-empty GUID.", nameof(offerCommandId));
        }

        var actor = McpActor.Require(httpContextAccessor);
        var conversationInstance = McpActor.Partition(actor, chatName);
        var chatJournalId = NeuronId.For<IChat>(brain.Owner, conversationInstance);
        var authorizationId = NeuronId.For<IMcpAuthorization>(
            brain.Owner,
            IMcpAuthorization.DefaultInstanceName);
        var offerCommand = new CommandId(offerId);
        var cursor = await brain.ReadJournalAsync(
            chatJournalId,
            JournalKind.Outgoing,
            afterSequence: long.MaxValue,
            cancellationToken);
        var resume = cursor.ResumeSequence;

        await brain.ActivateAsync(cancellationToken);
        using (VerifiedActor.Enter(actor))
        {
            // Match MapOwnerCommands KindChatButton — durable IButton grain, not IChat Fire.
            await brain.FireAsync<IButton>(
                ChatButtons.OfferedInstanceName(conversationInstance, offerCommand, buttonId),
                new ButtonClicked(offerCommand, buttonId, action),
                cancellationToken);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAfterAsync(
                chatJournalId,
                authorizationId,
                chatName,
                resume,
                timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer button '{buttonId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private async Task<ChatMessageResult> WaitForResponseAsync(
        NeuronId chatJournalId,
        NeuronId authorizationId,
        string chatName,
        CommandId commandId,
        CancellationToken cancellationToken)
    {
        long authorizationCursor = 0;

        await foreach (var page in brain.WatchJournalAsync(
            chatJournalId,
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
                if (delivery.Synapse is Responded response
                    && response.CommandId == commandId)
                {
                    return ToResult(chatName, response, delivery);
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }

    private async Task<ChatMessageResult> WaitForResponseAfterAsync(
        NeuronId chatJournalId,
        NeuronId authorizationId,
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        long authorizationCursor = 0;

        await foreach (var page in brain.WatchJournalAsync(
            chatJournalId,
            JournalKind.Outgoing,
            afterSequence,
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
                if (delivery.Synapse is Responded response)
                {
                    return ToResult(chatName, response, delivery);
                }
            }
        }

        throw new InvalidOperationException("The journal watch ended before the assistant responded.");
    }

    private static ChatMessageResult ToResult(
        string chatName,
        Responded response,
        SynapseDelivery delivery)
        => new(
            chatName,
            response.CommandId.ToString(),
            delivery.CorrelationId.Value.ToString("N"),
            response.Text,
            delivery.Sequence,
            delivery.Timestamp,
            response.Buttons is null
                ? null
                : [.. response.Buttons.Select(static b => new ChatButtonOfferResult(b.ButtonId, b.Label, b.Action))],
            response.Charts is null
                ? null
                : [.. response.Charts.Select(static c => new ChatChartOfferResult(
                    c.Title,
                    [.. c.Points.Select(static p => new ChatChartPointResult(p.Label, p.Value))],
                    c.ChartKind))]);

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

    [McpServerTool(Name = ConversationMcpSurface.ReadChatTranscript)]
    [Description("Read the durable transcript of a conversation owned by the authenticated caller.")]
    public async Task<ChatTranscriptPage> ReadChatTranscriptAsync(
        [Description("Conversation local name, for example 'main'")] string chatName = "main",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);
        var conversationInstance = McpActor.Partition(actor, chatName);
        cancellationToken.ThrowIfCancellationRequested();
        var page = await brain.GetGrainProxy<IConversation>(conversationInstance).Read();

        return new ChatTranscriptPage(
            chatName,
            [
                .. page.Turns.Select(turn => new ChatTranscriptTurn(
                    string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase) ? "you" : "brain",
                    turn.Text)),
            ]);
    }

}
