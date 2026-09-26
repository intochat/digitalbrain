namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56Terra : LLMModel<IGpt56Terra>
{
    public override string Id => "gpt-5.6-terra";

    public override AiProvider Provider => AiProvider.OpenAI;
}

[Alias("ai.llm.gpt56terra"), Orleans.Metadata.DefaultGrainType("ai.llm.gpt56terra")]
public interface IGpt56Terra : ILLM;