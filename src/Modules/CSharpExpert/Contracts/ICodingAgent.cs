using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[Alias("csharp-expert.coding-agent")]
public interface ICodingAgent : INeuron
{
    Task<string> Ask(string prompt, CancellationToken cancellationToken = default);
}
