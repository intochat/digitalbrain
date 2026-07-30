using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Chat;

[ClientEntryPoint]
[Alias("chat")]
[Description("Owner conversation neuron")]
public partial interface IChat : INeuron
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
