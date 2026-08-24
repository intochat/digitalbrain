namespace DigitalBrain.AI.Anthropic;

public sealed class Opus5 : LLMModel<IOpus5>
{
    public override string Id => "claude-opus-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

public interface IOpus5 : ILLM;
