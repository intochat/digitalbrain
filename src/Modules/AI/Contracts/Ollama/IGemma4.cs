namespace DigitalBrain.AI.Ollama;

public sealed class Gemma4 : LLMModel<IGemma4>
{
    // The E2B edge profile keeps the complete Gemma 4 instruction/tool surface while fitting
    // inside the 8 GB Docker Desktop budget used by the native development AppHost. The 12B
    // workstation profile needs more than 9 GB once llama.cpp repacking and KV caches are loaded.
    public override string Id => "gemma4:e2b";

    public override AiProvider Provider => AiProvider.Ollama;

    // gemma4 via Ollama supports native tool/function calling (2026). Vision is
    // input only — every gemma4 tag accepts images alongside text and answers in
    // text; it cannot produce an image, which is why there is no gemma4 entry in
    // the ImageModel catalogue.
    public override LlmCapabilities Capabilities => LlmCapabilities.Tools | LlmCapabilities.Vision;
}

public interface IGemma4 : ILLM;
