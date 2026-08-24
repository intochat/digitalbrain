namespace DigitalBrain.AI;

public abstract class ImageModel : AiModel
{
    /// <summary>Media type the provider returns for this model.</summary>
    public virtual string MediaType => "image/png";

    /// <summary>
    /// Whether this model accepts an explicit response format on the request.
    /// </summary>
    /// <remarks>
    /// Defaults to false because the newer models always return base64 and reject
    /// the parameter outright — sending it to gpt-image-1 is an HTTP 400
    /// (invalid_request_error: unknown_parameter), which is exactly how this was
    /// found. Older models such as the dall-e family must opt in, since without
    /// the parameter they return a URL instead of bytes.
    /// </remarks>
    public virtual bool AcceptsResponseFormat => false;

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
