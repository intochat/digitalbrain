namespace DigitalBrain.AI.Google;

public sealed class Gemini36Flash : LLMModel<IGemini36Flash>
{
    public override string Id => "gemini-3.6-flash";

    public override LlmProvider Provider => LlmProvider.Google;
}

public interface IGemini36Flash : ILLM;
