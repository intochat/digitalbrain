namespace DigitalBrain.AI.Anthropic;

public sealed class Opus5 : LLMModel<IOpus5>
{
    public override string Id => "claude-opus-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

[Alias("ai.llm.opus5"), Orleans.Metadata.DefaultGrainType("ai.llm.opus5")]
public interface IOpus5 : ILLM;