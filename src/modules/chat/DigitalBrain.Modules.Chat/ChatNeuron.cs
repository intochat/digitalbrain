using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Chat;

[GrainType("chat")]
internal sealed class ChatNeuron :
    Neuron,
    IChat,
    IHandle<AssistantAnswered>,
    IEmit<UserMessaged>,
    IEmit<AssistantResponded>
{
    private const string CommandOrderName = "chat.command-order";
    private const string CommandsName = "chat.commands";
    private const string TranscriptName = "chat.transcript";
    private const int RememberedCommands = 64;
    private const int RetainedTurns = 64;

    private readonly IDurableList<Guid> _commandOrder;
    private readonly IDurableDictionary<Guid, byte[]> _commands;
    private readonly IDurableList<byte[]> _transcript;
    private readonly Serializer<string> _texts;
    private readonly Serializer<ChatTurn> _turns;

    public ChatNeuron()
    {
        _commandOrder = ServiceProvider.GetRequiredKeyedService<IDurableList<Guid>>(CommandOrderName);
        _commands = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(CommandsName);
        _transcript = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TranscriptName);
        _texts = ServiceProvider.GetRequiredService<Serializer<string>>();
        _turns = ServiceProvider.GetRequiredService<Serializer<ChatTurn>>();
    }

    public async Task Send(SendMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
        if (message.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "The command id cannot be empty.",
                nameof(message));
        }

        if (_commands.TryGetValue(message.CommandId.Value, out var serialized))
        {
            if (!string.Equals(
                    _texts.Deserialize(serialized),
                    message.Text,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A chat command id cannot be reused with different text.");
            }

            return;
        }

        Remember(message.CommandId, message.Text);
        Remember(new ChatTurn(FromUser: true, message.Text));
        await EmitAsync(new UserMessaged(message.CommandId, Id, message.Text, Turns()));
    }

    public async Task HandleAsync(AssistantAnswered synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Text))
        {
            return;
        }

        Remember(new ChatTurn(FromUser: false, synapse.Text));
        await EmitAsync(new AssistantResponded(synapse.CommandId, Id, synapse.Text));
    }

    public Task<ChatTranscript> Read() => Task.FromResult(new ChatTranscript(Turns()));

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(_turns.Deserialize)];

    private void Remember(CommandId commandId, string text)
    {
        _commands[commandId.Value] = _texts.SerializeToArray(text);
        _commandOrder.Add(commandId.Value);

        while (_commandOrder.Count > RememberedCommands)
        {
            _commands.Remove(_commandOrder[0]);
            _commandOrder.RemoveAt(0);
        }
    }

    private void Remember(ChatTurn turn)
    {
        _transcript.Add(_turns.SerializeToArray(turn));

        while (_transcript.Count > RetainedTurns)
        {
            _transcript.RemoveAt(0);
        }
    }
}
