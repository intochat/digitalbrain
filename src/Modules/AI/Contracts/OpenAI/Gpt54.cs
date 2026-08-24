namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt54 : LLMModel<IGpt54>
{
    public override string Id => "gpt-5.4";

    public override AiProvider Provider => AiProvider.OpenAI;
}

public interface IGpt54 : ILLM;
