using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[Alias("ai.agent")]
public interface IAgent : INeuron
{
    [Alias("Ask")]
    Task<string> AskAsync(string prompt);
}
