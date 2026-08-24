namespace DigitalBrain.AI.OpenAI;

public sealed class GptImage1 : ImageModel<IGptImage1>
{
    public override string Id => "gpt-image-1";

    public override AiProvider Provider => AiProvider.OpenAI;
}

public interface IGptImage1 : IImageModel;
