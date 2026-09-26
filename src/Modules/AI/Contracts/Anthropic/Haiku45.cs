namespace DigitalBrain.AI.Anthropic;

public sealed class Haiku45 : LLMModel<IHaiku45>
{
    public override string Id => "claude-haiku-4-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

[Alias("ai.llm.haiku45"), Orleans.Metadata.DefaultGrainType("ai.llm.haiku45")]
public interface IHaiku45 : ILLM;