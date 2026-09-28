namespace DigitalBrain.AI.Anthropic;

public sealed class Sonnet5 : LLMModel<ISonnet5>
{
    public override string Id => "claude-sonnet-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

[Alias("ai.llm.sonnet5"), Orleans.Metadata.DefaultGrainType("ai.llm.sonnet5")]
public interface ISonnet5 : ILLM;