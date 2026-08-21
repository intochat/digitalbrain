using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DigitalBrain.AI.PersonaPlex;

internal sealed class PersonaPlexModelSet : IDisposable
{
    private bool _disposed;

    private PersonaPlexModelSet(
        InferenceSession encoder,
        InferenceSession temporal,
        InferenceSession depformer,
        InferenceSession decoder,
        OrtMemoryInfo cudaMemoryInfo,
        OrtAllocator temporalCudaAllocator,
        TensorElementType languageModelElementType)
    {
        Encoder = encoder;
        Temporal = temporal;
        Depformer = depformer;
        Decoder = decoder;
        CudaMemoryInfo = cudaMemoryInfo;
        TemporalCudaAllocator = temporalCudaAllocator;
        LanguageModelElementType = languageModelElementType;
    }

    internal InferenceSession Encoder { get; }

    internal InferenceSession Temporal { get; }

    internal InferenceSession Depformer { get; }

    internal InferenceSession Decoder { get; }

    internal OrtMemoryInfo CudaMemoryInfo { get; }

    internal OrtAllocator TemporalCudaAllocator { get; }

    internal TensorElementType LanguageModelElementType { get; }

    internal bool TemporalUsesPositionIds => Temporal.InputMetadata.ContainsKey("position_ids");

    internal static PersonaPlexModelSet Load(PersonaPlexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The enabled PersonaPlex runtime requires Windows and NVIDIA CUDA.");
        }

        if (!OrtEnv.Instance().GetAvailableProviders().Contains("CUDAExecutionProvider", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The enabled PersonaPlex runtime requires the ONNX Runtime CUDA execution provider.");
        }

        InferenceSession? encoder = null;
        InferenceSession? temporal = null;
        InferenceSession? depformer = null;
        InferenceSession? decoder = null;
        OrtMemoryInfo? cudaMemoryInfo = null;
        OrtAllocator? temporalCudaAllocator = null;

        try
        {
            encoder = CreateCudaSession(options.EncoderGraphPath, options.CudaDeviceId);
            temporal = CreateCudaSession(options.TemporalGraphPath, options.CudaDeviceId);
            depformer = CreateCudaSession(options.DepformerGraphPath, options.CudaDeviceId);
            decoder = CreateCudaSession(options.DecoderGraphPath, options.CudaDeviceId);

            var languageModelElementType = PersonaPlexModelManifest.Validate(
                encoder,
                temporal,
                depformer,
                decoder);

            cudaMemoryInfo = new OrtMemoryInfo(
                OrtMemoryInfo.allocatorCUDA,
                OrtAllocatorType.ArenaAllocator,
                options.CudaDeviceId,
                OrtMemType.Default);
            temporalCudaAllocator = new OrtAllocator(temporal, cudaMemoryInfo);

            return new PersonaPlexModelSet(
                encoder,
                temporal,
                depformer,
                decoder,
                cudaMemoryInfo,
                temporalCudaAllocator,
                languageModelElementType);
        }
        catch
        {
            temporalCudaAllocator?.Dispose();
            cudaMemoryInfo?.Dispose();
            decoder?.Dispose();
            depformer?.Dispose();
            temporal?.Dispose();
            encoder?.Dispose();
            throw;
        }
    }

    internal async ValueTask WarmUpAsync(CancellationToken cancellationToken)
    {
        await using var session = new PersonaPlexSession(this);
        var silence = PersonaPlexAudioFrame.Create(0, new short[PersonaPlexSession.FrameSampleCount]);

        for (var frame = 0; frame < 3; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await session.ProcessAsync(silence, cancellationToken).ConfigureAwait(false);
        }

        await session.ResetAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TemporalCudaAllocator.Dispose();
        CudaMemoryInfo.Dispose();
        Decoder.Dispose();
        Depformer.Dispose();
        Temporal.Dispose();
        Encoder.Dispose();
    }

    private static InferenceSession CreateCudaSession(string graphPath, int cudaDeviceId)
    {
        using var cudaOptions = new OrtCUDAProviderOptions();
        cudaOptions.UpdateOptions(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["device_id"] = cudaDeviceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["use_tf32"] = "0",
        });

        using var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING,
        };
        sessionOptions.AppendExecutionProvider_CUDA(cudaOptions);

        return new InferenceSession(graphPath, sessionOptions);
    }
}

internal static class PersonaPlexModelManifest
{
    internal const int TemporalLayerCount = 32;
    internal const int TemporalHeadCount = 32;
    internal const int TemporalHeadDimension = 128;
    internal const int DepformerLayerCount = 6;
    internal const int DepformerHeadCount = 16;
    internal const int DepformerHeadDimension = 64;

    internal static TensorElementType Validate(
        InferenceSession encoder,
        InferenceSession temporal,
        InferenceSession depformer,
        InferenceSession decoder)
    {
        RequireTensor("mimi_encoder", encoder.InputMetadata, "waveform", [null, 1, null], TensorElementType.Float);
        RequireTensor("mimi_encoder", encoder.OutputMetadata, "codes", [null, 8, null], TensorElementType.Int64);

        ValidateTemporalNames(
            temporal.InputMetadata.Keys.ToHashSet(StringComparer.Ordinal),
            temporal.OutputMetadata.Keys.ToHashSet(StringComparer.Ordinal));
        RequireTensor("temporal", temporal.InputMetadata, "input_frame", [null, 17, null], TensorElementType.Int64);
        RequireTensor("temporal", temporal.InputMetadata, "attention_mask", [null, null], TensorElementType.Int64);
        if (temporal.InputMetadata.ContainsKey("position_ids"))
        {
            RequireTensor("temporal", temporal.InputMetadata, "position_ids", [null, null], TensorElementType.Int64);
        }

        var languageModelElementType = RequireFloatingTensor(
            "temporal",
            temporal.InputMetadata,
            "past_key_values.0.key",
            [null, TemporalHeadCount, null, TemporalHeadDimension]);
        RequireTensor("temporal", temporal.OutputMetadata, "hidden", [null, null, 4096], languageModelElementType);
        RequireTensor("temporal", temporal.OutputMetadata, "text_logits", [null, null, 32000], languageModelElementType);

        for (var layer = 0; layer < TemporalLayerCount; layer++)
        {
            RequireTensor("temporal", temporal.InputMetadata, $"past_key_values.{layer}.key", [null, TemporalHeadCount, null, TemporalHeadDimension], languageModelElementType);
            RequireTensor("temporal", temporal.InputMetadata, $"past_key_values.{layer}.value", [null, TemporalHeadCount, null, TemporalHeadDimension], languageModelElementType);
            RequireTensor("temporal", temporal.OutputMetadata, $"present.{layer}.key", [null, TemporalHeadCount, null, TemporalHeadDimension], languageModelElementType);
            RequireTensor("temporal", temporal.OutputMetadata, $"present.{layer}.value", [null, TemporalHeadCount, null, TemporalHeadDimension], languageModelElementType);
        }

        ValidateDepformerNames(
            depformer.InputMetadata.Keys.ToHashSet(StringComparer.Ordinal),
            depformer.OutputMetadata.Keys.ToHashSet(StringComparer.Ordinal));
        RequireTensor("depformer", depformer.InputMetadata, "hidden", [null, 1, 4096], languageModelElementType);
        RequireTensor("depformer", depformer.InputMetadata, "prev_token", [null, 1], TensorElementType.Int64);
        RequireTensor("depformer", depformer.InputMetadata, "substep_index", [], TensorElementType.Int64);
        RequireTensor("depformer", depformer.OutputMetadata, "logits", [null, 1, 2048], languageModelElementType);

        for (var layer = 0; layer < DepformerLayerCount; layer++)
        {
            RequireTensor("depformer", depformer.InputMetadata, $"past_key_values.{layer}.key", [null, DepformerHeadCount, null, DepformerHeadDimension], languageModelElementType);
            RequireTensor("depformer", depformer.InputMetadata, $"past_key_values.{layer}.value", [null, DepformerHeadCount, null, DepformerHeadDimension], languageModelElementType);
            RequireTensor("depformer", depformer.OutputMetadata, $"present.{layer}.key", [null, DepformerHeadCount, null, DepformerHeadDimension], languageModelElementType);
            RequireTensor("depformer", depformer.OutputMetadata, $"present.{layer}.value", [null, DepformerHeadCount, null, DepformerHeadDimension], languageModelElementType);
        }

        RequireTensor("mimi_decoder", decoder.InputMetadata, "codes", [null, 8, null], TensorElementType.Int64);
        RequireTensor("mimi_decoder", decoder.OutputMetadata, "waveform", [null, 1, null], TensorElementType.Float);

        return languageModelElementType;
    }

    internal static void ValidateTemporalNames(
        IReadOnlySet<string> inputNames,
        IReadOnlySet<string> outputNames)
    {
        ArgumentNullException.ThrowIfNull(inputNames);
        ArgumentNullException.ThrowIfNull(outputNames);

        var requiredInputs = new List<string> { "input_frame", "attention_mask" };
        var requiredOutputs = new List<string> { "hidden", "text_logits" };

        for (var layer = 0; layer < TemporalLayerCount; layer++)
        {
            requiredInputs.Add($"past_key_values.{layer}.key");
            requiredInputs.Add($"past_key_values.{layer}.value");
            requiredOutputs.Add($"present.{layer}.key");
            requiredOutputs.Add($"present.{layer}.value");
        }

        ThrowIfMissing("temporal", requiredInputs, inputNames, "input");
        ThrowIfMissing("temporal", requiredOutputs, outputNames, "output");
    }

    private static void ValidateDepformerNames(
        IReadOnlySet<string> inputNames,
        IReadOnlySet<string> outputNames)
    {
        var requiredInputs = new List<string> { "hidden", "prev_token", "substep_index" };
        var requiredOutputs = new List<string> { "logits" };

        for (var layer = 0; layer < DepformerLayerCount; layer++)
        {
            requiredInputs.Add($"past_key_values.{layer}.key");
            requiredInputs.Add($"past_key_values.{layer}.value");
            requiredOutputs.Add($"present.{layer}.key");
            requiredOutputs.Add($"present.{layer}.value");
        }

        ThrowIfMissing("depformer", requiredInputs, inputNames, "input");
        ThrowIfMissing("depformer", requiredOutputs, outputNames, "output");
    }

    private static TensorElementType RequireFloatingTensor(
        string graphName,
        IReadOnlyDictionary<string, NodeMetadata> tensors,
        string tensorName,
        int?[] dimensions)
    {
        if (!tensors.TryGetValue(tensorName, out var metadata)
            || !metadata.IsTensor
            || metadata.ElementDataType is not (TensorElementType.Float or TensorElementType.Float16))
        {
            throw Incompatible(graphName, $"tensor '{tensorName}' must be FLOAT or FLOAT16");
        }

        ValidateDimensions(graphName, tensorName, metadata, dimensions);
        return metadata.ElementDataType;
    }

    private static void RequireTensor(
        string graphName,
        IReadOnlyDictionary<string, NodeMetadata> tensors,
        string tensorName,
        int?[] dimensions,
        TensorElementType elementType)
    {
        if (!tensors.TryGetValue(tensorName, out var metadata)
            || !metadata.IsTensor
            || metadata.ElementDataType != elementType)
        {
            throw Incompatible(graphName, $"tensor '{tensorName}' must be {elementType}");
        }

        ValidateDimensions(graphName, tensorName, metadata, dimensions);
    }

    private static void ValidateDimensions(
        string graphName,
        string tensorName,
        NodeMetadata metadata,
        int?[] expectedDimensions)
    {
        if (metadata.Dimensions.Length != expectedDimensions.Length)
        {
            throw Incompatible(graphName, $"tensor '{tensorName}' has the wrong rank");
        }

        for (var dimension = 0; dimension < expectedDimensions.Length; dimension++)
        {
            var expected = expectedDimensions[dimension];
            if (expected.HasValue && metadata.Dimensions[dimension] != expected.Value)
            {
                throw Incompatible(
                    graphName,
                    $"tensor '{tensorName}' dimension {dimension} must be {expected.Value}");
            }
        }
    }

    private static void ThrowIfMissing(
        string graphName,
        IEnumerable<string> requiredNames,
        IReadOnlySet<string> actualNames,
        string kind)
    {
        var missing = requiredNames.Where(name => !actualNames.Contains(name)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        throw Incompatible(graphName, $"missing {kind}(s): {string.Join(", ", missing)}");
    }

    private static PersonaPlexModelManifestException Incompatible(string graphName, string detail)
        => new($"PersonaPlex model-manifest incompatibility for {graphName}: {detail}.");
}

internal sealed class PersonaPlexModelManifestException(string message) : InvalidOperationException(message);
