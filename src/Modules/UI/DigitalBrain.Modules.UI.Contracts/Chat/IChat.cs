using DigitalBrain.Abstractions;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Chat;

[ClientEntryPoint]
[Alias("chat")]
public partial interface IChat :
    INeuron,
    IHandle<ReadTranscriptRequest>,
    IHandle<Note>,
    IHandle<TimerCard>
{
    [Alias(nameof(Send))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Send(SendMessage message);

    [Alias(nameof(SendStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Read))]
    Task<ChatTranscript> Read();
}
