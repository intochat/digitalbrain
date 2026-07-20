using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[Alias("ai.llm")]
public interface ILLM : INeuron
{
    [Alias("Ask")]
    Task<string> AskAsync(string prompt);
}
