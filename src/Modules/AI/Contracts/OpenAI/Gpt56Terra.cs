namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56Terra : LLMModel<IGpt56Terra>
{
    public override string Id => "gpt-5.6-terra";

    public override AiProvider Provider => AiProvider.OpenAI;
}

public interface IGpt56Terra : ILLM;
