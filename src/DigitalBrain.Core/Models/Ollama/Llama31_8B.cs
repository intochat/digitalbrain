namespace DigitalBrain.Core.Models.Ollama;

public sealed class Llama31_8B : LlmModel
{
    public override string Provider => DigitalBrainProviderIds.Ollama;
    public override string Id => "llama3.1:8b";
    public override string DisplayName => "Llama 3.1 8B";
    public override DigitalBrainModelCapabilities Capabilities => DigitalBrainModelCapabilities.ToolCapable;
}
