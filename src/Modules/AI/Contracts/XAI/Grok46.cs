namespace DigitalBrain.AI.XAI;

public sealed class Grok46 : LLMModel<IGrok46>
{
    public override string Id => "grok-4.6";

    public override AiProvider Provider => AiProvider.XAI;
}

[Alias("ai.llm.grok46"), Orleans.Metadata.DefaultGrainType("ai.llm.grok46")]
public interface IGrok46 : ILLM;