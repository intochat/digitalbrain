namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt54Nano : LLMModel<IGpt54Nano>
{
    public override string Id => "gpt-5.4-nano";

    public override AiProvider Provider => AiProvider.OpenAI;
}

public interface IGpt54Nano : ILLM;
