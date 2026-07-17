using Brain.Contracts;

namespace AI.Contracts;

public sealed record TextGenerationRequest(
    string Instruction,
    string Input,
    int MaximumOutputTokens = 512);

public sealed record TextGenerationResult(string Text);

public interface ITextGenerationNeuron : INeuronContract
{
    [NeuronContract(AiCapabilityIds.TextGenerate)]
    Task<TextGenerationResult> GenerateAsync(TextGenerationRequest request);
}
