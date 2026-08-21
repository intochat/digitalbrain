namespace DigitalBrain.AI.PersonaPlex;

public sealed class PersonaPlexOptions
{
    public const string SectionName = "DigitalBrain:AI:PersonaPlex";

    public bool Enabled { get; set; }

    public string ModelDirectory { get; set; } = string.Empty;

    public int CudaDeviceId { get; set; }

    public int MaxSessions { get; set; } = 1;

    /// <summary>True when inference is delegated to the private Aspire runtime adapter.</summary>
    public bool UseRemoteRuntime { get; set; }

    /// <summary>Private adapter base URI injected by Aspire for Kernel use only.</summary>
    public string RuntimeEndpoint { get; set; } = string.Empty;

    /// <summary>Credential for the Kernel-to-adapter stream; never expose this to clients.</summary>
    public string AdapterToken { get; set; } = string.Empty;

    public string EncoderGraphPath => Path.Combine(ModelDirectory, "mimi_encoder", "model.onnx");

    public string TemporalGraphPath => Path.Combine(ModelDirectory, "temporal", "model.onnx");

    public string DepformerGraphPath => Path.Combine(ModelDirectory, "depformer", "model.onnx");

    public string DecoderGraphPath => Path.Combine(ModelDirectory, "mimi_decoder", "model.onnx");

    public void Validate()
    {
        if (MaxSessions <= 0)
        {
            throw new InvalidOperationException("PersonaPlex requires at least one session.");
        }

        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ModelDirectory))
        {
            throw new InvalidOperationException("PersonaPlex is enabled but its model directory is not configured.");
        }

        if (CudaDeviceId < 0)
        {
            throw new InvalidOperationException("PersonaPlex requires a non-negative CUDA device ID.");
        }

        var missingGraphs = new[]
        {
            (Path: EncoderGraphPath, Name: "mimi_encoder/model.onnx"),
            (Path: TemporalGraphPath, Name: "temporal/model.onnx"),
            (Path: DepformerGraphPath, Name: "depformer/model.onnx"),
            (Path: DecoderGraphPath, Name: "mimi_decoder/model.onnx"),
        }.Where(static graph => !File.Exists(graph.Path)).Select(static graph => graph.Name).ToArray();

        if (missingGraphs.Length > 0)
        {
            throw new InvalidOperationException(
                $"PersonaPlex model configuration is missing required graph files: {string.Join(", ", missingGraphs)}.");
        }
    }
}
