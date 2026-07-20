using DigitalBrain.Abstractions;

namespace DigitalBrain.Modules.AI.Contracts;

[Alias("db.ai.chat-model")]
public interface IChatModel : INeuron
{
    [Alias("Complete")]
    Task<string> CompleteAsync(string prompt);
}
