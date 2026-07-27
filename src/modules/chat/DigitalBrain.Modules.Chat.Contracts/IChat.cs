using DigitalBrain.Abstractions;

namespace DigitalBrain.Chat;

[ClientEntryPoint]
public partial interface IChat : INeuron
{
    [Alias(nameof(Send))]
    Task Send(SendMessage message);

    [Alias(nameof(Read))]
    Task<ChatTranscript> Read();
}
