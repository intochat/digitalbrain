namespace DigitalBrain.AI.Ollama;

public sealed class Gemma4 : LLMModel<IGemma4>
{
    public override string Id => "gemma4:12b";

    public override LlmProvider Provider => LlmProvider.Ollama;

    // gemma4 via Ollama supports native tool/function calling (2026).
    public override bool SupportsTools => true;
}

public interface IGemma4 : ILLM;
