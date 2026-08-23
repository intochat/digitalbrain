namespace DigitalBrain.AI.Anthropic;

public sealed class Haiku45 : LLMModel<IHaiku45>
{
    public override string Id => "claude-haiku-4-5";

    public override LlmProvider Provider => LlmProvider.Anthropic;
}

public interface IHaiku45 : ILLM;
