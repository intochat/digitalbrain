namespace DigitalBrain.AI.Google;

public sealed class Gemini36Flash : LLMModel<IGemini36Flash>
{
    public override string Id => "gemini-3.6-flash";

    public override AiProvider Provider => AiProvider.Google;
}

[Alias("ai.llm.gemini36flash"), Orleans.Metadata.DefaultGrainType("ai.llm.gemini36flash")]
public interface IGemini36Flash : ILLM;