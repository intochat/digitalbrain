using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.OS.Mcp;

[McpServerToolType]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the MCP server DI container via WithTools<DigitalBrainMcpTools>().")]
internal sealed class DigitalBrainMcpTools(IDigitalBrain brain, IGrainFactory grains)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
    private const int DefaultTimeoutSeconds = 300;
    private const int MaximumTimeoutSeconds = 300;

    [McpServerTool(Name = McpHost.SendChatMessageToolName)]
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

        if (!Guid.TryParse(commandId, out var commandIdentity)
            || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException(
                "The command id must be a non-empty GUID.",
                nameof(commandId));
        }

        var chatId = NeuronId.For<IChat>(brain.Owner, chatName);
        var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(brain.Owner).ToGrainId());
        var command = new CommandId(commandIdentity);

        await brain.Get<IChat>(chatName).Send(new SendMessage(command, text));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(
                session,
                chatId,
                chatName,
                command,
                afterSequence: 0,
                cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"DigitalBrain did not answer command '{commandId}' in conversation "
                + $"'{chatName}' within {timeoutSeconds} seconds.");
        }
    }

    private static async Task<ChatMessageResult> WaitForResponseAsync(
        ISessionNeuron session,
        NeuronId chatId,
        string chatName,
        CommandId commandId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        var cursor = afterSequence;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await session.ReadNeuronJournal(chatId, JournalKind.Outgoing, cursor);

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

            cursor = page.ResumeSequence;
            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
