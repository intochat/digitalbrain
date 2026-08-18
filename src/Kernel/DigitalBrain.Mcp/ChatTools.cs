using System.ComponentModel;
using DigitalBrain.Abstractions;
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

    // Stable MCP operator principal (legacy default).
    internal static readonly PrincipalId OperatorPrincipal =
        new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    // Second principal for Wave 3 isolation tests.
    internal static readonly PrincipalId AlicePrincipal =
        new(Guid.Parse("00000000-0000-4000-8000-0000000000a2"));

    internal static readonly PrincipalId BobPrincipal =
        new(Guid.Parse("00000000-0000-4000-8000-0000000000b0"));

    [McpServerTool(Name = McpSurface.SendChatMessage)]
    [Description(
        "Send a message through a principal-partitioned conversation and wait for the assistant "
        + "response. principalKey selects alice|bob|operator (default operator).")]
    public async Task<ChatMessageResult> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Caller-generated command id used to resume an interrupted call")]
        string commandId,
        [Description("Conversation name, for example 'main'")] string chatName = "main",
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
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

        var (principal, username) = ResolvePrincipal(principalKey);
        var chatInstance = PrincipalPartition.InstanceName(principal, chatName);
        var chatId = NeuronId.For<IChat>(brain.Owner, chatInstance);
        var command = new CommandId(commandIdentity);

        await brain.ActivateAsync(cancellationToken);
        var actor = new ActorContext(principal, username);
        await brain.GetGrainProxy<IChat>(chatInstance).Send(new SendMessage(command, text, actor));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(chatId, chatName, command, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' for principal '{principalKey}' within {timeoutSeconds} seconds.");
        }
    }

    [McpServerTool(Name = McpSurface.ActivateChatButton)]
    [Description(
        "Activate a button previously offered on an assistant chat turn. "
        + "Uses the same durable ButtonClicked path as the product UI command bus.")]
    public async Task<ChatMessageResult> ActivateChatButtonAsync(
        [Description("Conversation name, for example 'main'")] string chatName,
        [Description("Command id of the assistant turn that offered the button")] string offerCommandId,
        [Description("Button id from the offer")] string buttonId,
        [Description("Action from the offer, for example a sign-in URL or command name")] string action,
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
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

        var (principal, _) = ResolvePrincipal(principalKey);
        var chatInstance = PrincipalPartition.InstanceName(principal, chatName);
        var chatId = NeuronId.For<IChat>(brain.Owner, chatInstance);
        var cursor = await brain.ReadJournalAsync(chatId, JournalKind.Outgoing, afterSequence: long.MaxValue, cancellationToken);
        var resume = cursor.ResumeSequence;

        await brain.ActivateAsync(cancellationToken);
        await brain.FireAsync<IChat>(
            chatInstance,
            new ButtonClicked(new CommandId(offerId), buttonId, action),
            cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAfterAsync(chatId, chatName, resume, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer button '{buttonId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    internal static (PrincipalId Principal, string Username) ResolvePrincipal(string? principalKey)
        => (principalKey ?? "operator").Trim().ToLowerInvariant() switch
        {
            "alice" => (AlicePrincipal, "alice"),
            "bob" => (BobPrincipal, "bob"),
            "operator" or "" => (OperatorPrincipal, "operator"),
            _ => throw new ArgumentException(
                "principalKey must be operator, alice, or bob.",
                nameof(principalKey)),
        };

    private async Task<ChatMessageResult> WaitForResponseAsync(
        NeuronId chatId,
        string chatName,
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
        NeuronId chatId,
        string chatName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        await foreach (var page in brain.WatchJournalAsync(
            chatId,
            JournalKind.Outgoing,
            afterSequence,
            cancellationToken))
        {
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
}
