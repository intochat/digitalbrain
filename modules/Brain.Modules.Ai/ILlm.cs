using Brain.Contracts;
namespace Brain.Modules.Ai;

public interface ILlm : INeuronContract
{
    [NeuronContract("llm.complete.v1")]
    Task<LlmReply> CompleteAsync(LlmRequest request);
}

public sealed record LlmRequest(string Prompt, int? MaxOutputTokens = null);

public sealed record LlmReply(string Text, string Model, long Revision);
