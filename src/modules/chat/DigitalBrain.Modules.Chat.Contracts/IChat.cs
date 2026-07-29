using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Chat;

[ClientEntryPoint]
public partial interface IChat : INeuron
{
    [Alias(nameof(Send))]
    Task Send(SendMessage message);

    [Alias(nameof(SendStreaming))]
    IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Read))]
    Task<ChatTranscript> Read();
}
