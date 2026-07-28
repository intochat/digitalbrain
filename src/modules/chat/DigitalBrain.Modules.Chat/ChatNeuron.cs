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
    private const string TranscriptName = "chat.transcript";
    private const int RetainedTurns = 64;

    private readonly IDurableList<byte[]> _transcript;
    private readonly Serializer<ChatTurn> _turns;

    public ChatNeuron()
    {
        _transcript = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TranscriptName);
        _turns = ServiceProvider.GetRequiredService<Serializer<ChatTurn>>();
    }

    public async Task Send(SendMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);

        await RememberAsync(new ChatTurn(FromUser: true, message.Text));
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

        await RememberAsync(new ChatTurn(FromUser: false, synapse.Text));
        await EmitAsync(new AssistantResponded(synapse.CommandId, Id, synapse.Text));
    }

    public Task<ChatTranscript> Read() => Task.FromResult(new ChatTranscript(Turns()));

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(_turns.Deserialize)];

    private async Task RememberAsync(ChatTurn turn)
    {
        _transcript.Add(_turns.SerializeToArray(turn));

        while (_transcript.Count > RetainedTurns)
        {
            _transcript.RemoveAt(0);
        }

        await WriteStateAsync(CancellationToken.None);
    }
}
