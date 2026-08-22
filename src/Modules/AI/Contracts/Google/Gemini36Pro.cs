namespace DigitalBrain.AI.Google;

public sealed class Gemini36Pro : LLMModel<IGemini36Pro>
{
    public override string Id => "gemini-3.6-pro";

    public override LlmProvider Provider => LlmProvider.Google;
}

public interface IGemini36Pro : ILLM;
