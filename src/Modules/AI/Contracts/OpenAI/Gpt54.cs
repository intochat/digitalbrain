namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt54 : LLMModel<IGpt54>
{
    public override string Id => "gpt-5.4";

    public override AiProvider Provider => AiProvider.OpenAI;
}

[Alias("ai.llm.gpt54"), Orleans.Metadata.DefaultGrainType("ai.llm.gpt54")]
public interface IGpt54 : ILLM;