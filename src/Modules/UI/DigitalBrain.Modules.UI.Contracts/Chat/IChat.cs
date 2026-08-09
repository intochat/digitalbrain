using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Chat;

[ClientEntryPoint]
[Alias("chat")]
[Description("Owner conversation neuron — not bound to a single responder")]
public partial interface IChat :
    INeuron,
    IEmit<UserMessaged>,
    IEmit<Responded>,
    IHandle<ReadTranscriptRequest>,
    IHandle<ShowTime>,
    IEmit<TranscriptRead>
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
