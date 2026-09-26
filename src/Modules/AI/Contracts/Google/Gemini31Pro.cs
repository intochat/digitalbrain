namespace DigitalBrain.AI.Google;

public sealed class Gemini31Pro : LLMModel<IGemini31Pro>
{
    public override string Id => "gemini-3.1-pro-preview";

    public override AiProvider Provider => AiProvider.Google;
}

[Alias("ai.llm.gemini31pro"), Orleans.Metadata.DefaultGrainType("ai.llm.gemini31pro")]
public interface IGemini31Pro : ILLM;