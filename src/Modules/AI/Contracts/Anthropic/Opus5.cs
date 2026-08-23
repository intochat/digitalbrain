namespace DigitalBrain.AI.Anthropic;

public sealed class Opus5 : LLMModel<IOpus5>
{
    public override string Id => "claude-opus-5";

    public override LlmProvider Provider => LlmProvider.Anthropic;
}

public interface IOpus5 : ILLM;
