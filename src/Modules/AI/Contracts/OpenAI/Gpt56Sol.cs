namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56Sol : LLMModel<IGpt56Sol>
{
    public override string Id => "gpt-5.6-sol";

    public override AiProvider Provider => AiProvider.OpenAI;
}

[Orleans.Metadata.DefaultGrainType("ai.llm.gpt56sol")]
public interface IGpt56Sol : ILLM;