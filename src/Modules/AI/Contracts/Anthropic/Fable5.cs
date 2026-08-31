namespace DigitalBrain.AI.Anthropic;

public sealed class Fable5 : LLMModel<IFable5>
{
    public override string Id => "claude-fable-5";

    public override AiProvider Provider => AiProvider.Anthropic;
}

public interface IFable5 : ILLM;
