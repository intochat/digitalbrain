namespace DigitalBrain.AI.XAI;

public sealed class Grok46 : LLMModel<IGrok46>
{
    public override string Id => "grok-4.6";

    public override LlmProvider Provider => LlmProvider.XAI;
}

public interface IGrok46 : ILLM;
