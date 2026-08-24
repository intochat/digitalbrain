namespace DigitalBrain.AI.Anthropic;

public sealed class Sonnet5 : LLMModel<ISonnet5>
{
    public override string Id => "claude-sonnet-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

public interface ISonnet5 : ILLM;
