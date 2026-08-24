namespace DigitalBrain.AI.Ollama;

public sealed class Gemma4 : LLMModel<IGemma4>
{
    public override string Id => "gemma4:12b";

    public override AiProvider Provider => AiProvider.Ollama;

    // gemma4 via Ollama supports native tool/function calling (2026).
    public override LlmCapabilities Capabilities => LlmCapabilities.Tools;
}

public interface IGemma4 : ILLM;
