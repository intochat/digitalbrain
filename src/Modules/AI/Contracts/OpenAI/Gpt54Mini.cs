namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt54Mini : LLMModel<IGpt54Mini>
{
    public override string Id => "gpt-5.4-mini";

    public override LlmProvider Provider => LlmProvider.OpenAI;
}

public interface IGpt54Mini : ILLM;
