namespace DigitalBrain.AI.Ollama;

public sealed class Gemma4 : LLMModel<IGemma4>
{
    public override string Id => "gemma4:12b";

    public override LlmProvider Provider => LlmProvider.Ollama;

    public override bool SupportsTools => false;
}

public interface IGemma4 : ILLM;
