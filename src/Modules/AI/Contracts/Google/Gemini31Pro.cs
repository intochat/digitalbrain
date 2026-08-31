namespace DigitalBrain.AI.Google;

public sealed class Gemini31Pro : LLMModel<IGemini31Pro>
{
    public override string Id => "gemini-3.1-pro-preview";

    public override AiProvider Provider => AiProvider.Google;
}

public interface IGemini31Pro : ILLM;
