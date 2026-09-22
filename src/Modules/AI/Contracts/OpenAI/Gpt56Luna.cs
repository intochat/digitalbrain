namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56Luna : LLMModel<IGpt56Luna>
{
    public override string Id => "gpt-5.6-luna";

    public override AiProvider Provider => AiProvider.OpenAI;
}

[Orleans.Metadata.DefaultGrainType("ai.llm.gpt56luna")]
public interface IGpt56Luna : ILLM;