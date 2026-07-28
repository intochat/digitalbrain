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
    private const int DefaultTimeoutSeconds = 120;
    private const int MaximumTimeoutSeconds = 180;

    [McpServerTool(Name = McpHost.SendChatMessageToolName)]
    [Description(
        "Send a message through the owner's DigitalBrain conversation and wait for the exact "
        + "correlated assistant response. This is the same durable chat path used by the product UI.")]
    public async Task<ChatMessageResult> SendChatMessageAsync(
        [Description("Message to send to DigitalBrain")] string text,
        [Description("Conversation name, for example 'main'")] string chatName = "main",
        [Description("Maximum wait in seconds, from 1 through 180")]
        int timeoutSeconds = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            timeoutSeconds,
            MaximumTimeoutSeconds);

        var chatId = NeuronId.For<IChat>(brain.Owner, chatName);
        var session = grains.GetGrain<ISessionNeuron>(
            ISessionNeuron.ForOwner(brain.Owner).ToGrainId());
        var baseline = await session.ReadNeuronJournal(
            chatId,
            JournalKind.Outgoing,
            afterSequence: 0);
        var commandId = CommandId.New();

        await brain.Get<IChat>(chatName).Send(new SendMessage(commandId, text));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            return await WaitForResponseAsync(
                session,
                chatId,
                chatName,
                commandId,
                baseline.ResumeSequence,
                timeout.Token);
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
        CorrelationId? correlation = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await session.ReadNeuronJournal(
                chatId,
                JournalKind.Outgoing,
                cursor);

            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is UserMessaged messaged
                    && messaged.CommandId == commandId)
                {
                    correlation = delivery.CorrelationId;
                }

                if (delivery.Synapse is AssistantResponded response
                    && correlation is { } expected
                    && delivery.CorrelationId == expected)
                {
                    return new ChatMessageResult(
                        chatName,
                        commandId.ToString(),
                        expected.Value.ToString("N"),
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
