namespace DigitalBrain.AI;

public abstract class LLMModel
{
    public abstract string Id { get; }

    public abstract LlmProvider Provider { get; }

    public abstract Type Marker { get; }

    public virtual bool SupportsTools => true;

    public bool IsLocal => Provider == LlmProvider.Ollama;

    // Cloud models precede local ones: when no default is configured, the first
    // model whose provider has credentials becomes the default chat model.
    public static IReadOnlyList<LLMModel> All { get; } =
    [
        new OpenAI.Gpt54(),
        new OpenAI.Gpt54Mini(),
        new OpenAI.Gpt54Nano(),
        new Anthropic.Opus5(),
        new Anthropic.Sonnet5(),
        new Anthropic.Haiku45(),
        new Google.Gemini36Pro(),
        new Google.Gemini36Flash(),
        new XAI.Grok46(),
        new Ollama.Gemma4(),
    ];

    public static LLMModel? FindByMarker(Type marker)
        => All.FirstOrDefault(model => model.Marker == marker);

    public static LLMModel? FindByMarkerName(string markerName)
        => All.FirstOrDefault(model => string.Equals(model.Marker.Name, markerName, StringComparison.Ordinal));
}

public abstract class LLMModel<TMarker> : LLMModel
    where TMarker : ILLM
{
    public sealed override Type Marker => typeof(TMarker);
}
