namespace DigitalBrain.AI.Ollama;

public sealed class Qwen35 : LLMModel<IQwen35>
{
    // 4B fits the same ~8 GB Docker Desktop budget as gemma4:e2b while keeping
    // native tool calling and multimodal input for local agent work.
    public override string Id => "qwen3.5:4b";

    public override AiProvider Provider => AiProvider.Ollama;

    // Ollama ships qwen3.5 with native tool calling and vision input. Vision is
    // input only — the model answers in text and is not an image generator.
    public override LlmCapabilities Capabilities => LlmCapabilities.Tools | LlmCapabilities.Vision;
}

public interface IQwen35 : ILLM;
