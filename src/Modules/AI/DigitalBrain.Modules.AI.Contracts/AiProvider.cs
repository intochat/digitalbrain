namespace DigitalBrain.AI;

// Who serves a model. Spans every model kind, not just chat, so a locally
// hosted transcription model is describable in the same vocabulary as a cloud
// chat model.
public enum AiProvider
{
    OpenAI,
    Anthropic,
    Google,
    XAI,
    Ollama,
    FoundryLocal,
}
