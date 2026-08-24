namespace DigitalBrain.AI.Google;

public sealed class Gemini36Pro : LLMModel<IGemini36Pro>
{
    public override string Id => "gemini-3.6-pro";

    public override AiProvider Provider => AiProvider.Google;
}

public interface IGemini36Pro : ILLM;
