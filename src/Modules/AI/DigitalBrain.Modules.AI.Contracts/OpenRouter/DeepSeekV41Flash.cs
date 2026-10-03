namespace DigitalBrain.AI.OpenRouter;

public sealed class DeepSeekV41Flash : LLMModel<IDeepSeekV41Flash>
{
    public override string Id => "deepseek/deepseek-v4.1-flash";

    public override AiProvider Provider => AiProvider.OpenRouter;
}

[Alias("ai.llm.deepseek-v41-flash"), Orleans.Metadata.DefaultGrainType("ai.llm.deepseek-v41-flash")]
public interface IDeepSeekV41Flash : ILLM;
