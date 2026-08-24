namespace DigitalBrain.AI;

/// <summary>
/// Response shapes a transcription model can return.
/// </summary>
/// <remarks>
/// Not decoration: the gpt-4o transcribe models accept only text and json, while
/// verbose json and the subtitle formats are whisper-1's alone. Declaring it lets
/// a caller assert the format it wants is available instead of learning so from a
/// 400 at the provider.
/// </remarks>
[Flags]
public enum TranscriptionFormats
{
    None = 0,
    Text = 1,
    Json = 1 << 1,
    VerboseJson = 1 << 2,
    Srt = 1 << 3,
    Vtt = 1 << 4,
}

public abstract class TranscriptionModel : AiModel
{
    /// <summary>
    /// Defaults to plain text, which every transcription model can return.
    /// </summary>
    public virtual TranscriptionFormats Formats => TranscriptionFormats.Text;

    /// <summary>Whether timestamps can be requested from this model.</summary>
    public bool SupportsTimestamps => Formats.HasFlag(TranscriptionFormats.VerboseJson);

    // Hosted models precede local ones, and better local models precede weaker
    // ones: the Foundry service walks the local entries in order when its
    // configured model is absent from the machine's catalog. This ordering is
    // what the retired WhisperModel.Priority used to express.
    public static IReadOnlyList<TranscriptionModel> All { get; } =
    [
        new OpenAI.Gpt4oMiniTranscribe(),
        new OpenAI.Gpt4oTranscribe(),
        new OpenAI.Whisper1(),
        new FoundryLocal.WhisperLargeV3Turbo(),
        new FoundryLocal.WhisperSmall(),
        new FoundryLocal.WhisperTiny(),
    ];

    public static TranscriptionModel? FindByMarker(Type marker)
        => All.FirstOrDefault(model => model.Marker == marker);

    public static TranscriptionModel? FindByMarkerName(string markerName)
        => All.FirstOrDefault(model => string.Equals(model.Marker.Name, markerName, StringComparison.Ordinal));
}

public abstract class TranscriptionModel<TMarker> : TranscriptionModel
    where TMarker : ITranscription
{
    public sealed override Type Marker => typeof(TMarker);
}
