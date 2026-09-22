namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt54Mini : LLMModel<IGpt54Mini>
{
    public override string Id => "gpt-5.4-mini";

    public override AiProvider Provider => AiProvider.OpenAI;
}

[Orleans.Metadata.DefaultGrainType("ai.llm.gpt54mini")]
public interface IGpt54Mini : ILLM;