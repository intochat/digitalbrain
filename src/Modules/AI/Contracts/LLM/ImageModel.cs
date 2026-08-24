namespace DigitalBrain.AI;

public abstract class ImageModel : AiModel
{
    /// <summary>Media type the provider returns for this model.</summary>
    public virtual string MediaType => "image/png";

    public static IReadOnlyList<ImageModel> All { get; } =
    [
        new OpenAI.GptImage1(),
    ];

    public static ImageModel? FindByMarker(Type marker)
        => All.FirstOrDefault(model => model.Marker == marker);

    public static ImageModel? FindByMarkerName(string markerName)
        => All.FirstOrDefault(model => string.Equals(model.Marker.Name, markerName, StringComparison.Ordinal));
}

public abstract class ImageModel<TMarker> : ImageModel
    where TMarker : IImageModel
{
    public sealed override Type Marker => typeof(TMarker);
}
