using System.Buffers;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DigitalBrain.AI.PersonaPlex;

internal sealed class PersonaPlexSession : IPersonaPlexSession
{
    internal const int FrameSampleCount = 1920;

    private const int CodecWindowFrames = 8;
    private const int AudioCodebookCount = 8;
    private const int GeneratedAudioCodebookCount = 16;
    private const int ChannelCount = 17;
    private const int RingLength = 4;
    private const int InitialAudioToken = 2048;
    private const int InitialTextToken = 32000;
    private const long UngeneratedToken = -2;
    private const float TextTemperature = 0.7F;
    private const int TextTopK = 25;
    private const float AudioTemperature = 0.8F;
    private const int AudioTopK = 250;

    private static readonly int[] Delays = [0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1];
    private static readonly long[] EncoderInputShape = [1, 1, CodecWindowFrames * FrameSampleCount];
    private static readonly long[] DecoderInputShape = [1, AudioCodebookCount, CodecWindowFrames];
    private static readonly long[] TemporalFrameShape = [1, ChannelCount, 1];
    private static readonly long[] TemporalPositionShape = [1, 1];
    private static readonly long[] DepformerPreviousTokenShape = [1, 1];
    private static readonly long[] ScalarShape = [];
    private static readonly long[] TemporalEmptyCacheShape =
        [1, PersonaPlexModelManifest.TemporalHeadCount, 0, PersonaPlexModelManifest.TemporalHeadDimension];
    private static readonly long[] DepformerEmptyCacheShape =
        [1, PersonaPlexModelManifest.DepformerHeadCount, 0, PersonaPlexModelManifest.DepformerHeadDimension];
    private static readonly string[] EncoderInputNames = ["waveform"];
    private static readonly string[] EncoderOutputNames = ["codes"];
    private static readonly string[] DecoderInputNames = ["codes"];
    private static readonly string[] DecoderOutputNames = ["waveform"];
    private static readonly string[] DepformerOutputNames = CreateDepformerOutputNames();

    private readonly float[] _encoderWindow = new float[CodecWindowFrames * FrameSampleCount];
    private readonly long[] _decoderWindow = new long[AudioCodebookCount * CodecWindowFrames];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PersonaPlexModelSet _models;
    private readonly bool[] _provided = new bool[ChannelCount * RingLength];
    private readonly Random _random = new();
    private readonly long[] _ringCache = new long[ChannelCount * RingLength];
    private OrtValue[] _initialTemporalCache = [];
    private IDisposableReadOnlyCollection<OrtValue>? _temporalOutputs;
    private long _offset;
    private long _temporalPosition;
    private bool _disposed;

    internal PersonaPlexSession(PersonaPlexModelSet models)
    {
        _models = models;
        ResetCore();
    }

    public async ValueTask<PersonaPlexAudioFrame> ProcessAsync(
        PersonaPlexAudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            return ProcessFrame(frame);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ResetCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeTemporalState();
        }
        finally
        {
            _gate.Release();
        }
    }

    private PersonaPlexAudioFrame ProcessFrame(PersonaPlexAudioFrame frame)
    {
        ShiftEncoderWindow(frame.Pcm16.Span);
        var userCodes = EncodeNewestFrame();
        var assistantCodes = GenerateAssistantCodes(userCodes);
        var output = assistantCodes is null ? new short[FrameSampleCount] : DecodeNewestFrame(assistantCodes);
        return PersonaPlexAudioFrame.Create(frame.Sequence, output);
    }

    private void ShiftEncoderWindow(ReadOnlySpan<short> pcm16)
    {
        Array.Copy(
            _encoderWindow,
            FrameSampleCount,
            _encoderWindow,
            0,
            _encoderWindow.Length - FrameSampleCount);

        var target = _encoderWindow.AsSpan(_encoderWindow.Length - FrameSampleCount);
        for (var sample = 0; sample < pcm16.Length; sample++)
        {
            target[sample] = pcm16[sample] / 32768F;
        }
    }

    private long[] EncodeNewestFrame()
    {
        using var input = OrtValue.CreateTensorValueFromMemory(_encoderWindow, EncoderInputShape);
        using var runOptions = new RunOptions();
        using var outputs = _models.Encoder.Run(runOptions, EncoderInputNames, [input], EncoderOutputNames);

        var output = outputs[0];
        var shape = output.GetTensorTypeAndShape().Shape;
        if (shape.Length != 3 || shape[0] != 1 || shape[1] != AudioCodebookCount || shape[2] < 1)
        {
            throw new InvalidOperationException("PersonaPlex Mimi encoder returned an incompatible runtime shape.");
        }

        var sequenceLength = checked((int)shape[2]);
        var encoded = output.GetTensorDataAsSpan<long>();
        var newestCodes = new long[AudioCodebookCount];
        for (var codebook = 0; codebook < AudioCodebookCount; codebook++)
        {
            newestCodes[codebook] = encoded[(codebook * sequenceLength) + sequenceLength - 1];
        }

        return newestCodes;
    }

    private long[]? GenerateAssistantCodes(ReadOnlySpan<long> userCodes)
    {
        for (var codebook = 0; codebook < AudioCodebookCount; codebook++)
        {
            var channel = AudioCodebookCount + 1 + codebook;
            var writePosition = (int)((_offset + Delays[channel]) % RingLength);
            _ringCache[RingIndex(channel, writePosition)] = userCodes[codebook];
            _provided[RingIndex(channel, writePosition)] = true;
        }

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            if (_offset <= Delays[channel])
            {
                _ringCache[RingIndex(channel, (int)(_offset % RingLength))] =
                    channel == 0 ? InitialTextToken : InitialAudioToken;
                _provided[RingIndex(channel, (int)(_offset % RingLength))] = true;
            }
        }

        if (_offset == 0)
        {
            for (var channel = 0; channel < ChannelCount; channel++)
            {
                _ringCache[RingIndex(channel, 0)] = channel == 0 ? InitialTextToken : InitialAudioToken;
            }

            _offset++;
            return null;
        }

        var modelInputPosition = (int)((_offset - 1) % RingLength);
        var targetPosition = (int)(_offset % RingLength);
        var temporalFrame = new long[ChannelCount];
        var target = new long[ChannelCount];
        var provided = new bool[ChannelCount];
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            temporalFrame[channel] = _ringCache[RingIndex(channel, modelInputPosition)];
            target[channel] = _ringCache[RingIndex(channel, targetPosition)];
            provided[channel] = _provided[RingIndex(channel, targetPosition)];
        }

        var (hidden, textLogits) = RunTemporal(temporalFrame);
        var sampledText = SampleLogits(textLogits, TextTemperature, TextTopK);
        var nextText = provided[0] ? target[0] : sampledText;
        var sampledAudio = RunDepformer(nextText, hidden, target.AsSpan(1), provided.AsSpan(1));

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            _provided[RingIndex(channel, modelInputPosition)] = false;
        }

        if (!provided[0])
        {
            _ringCache[RingIndex(0, targetPosition)] = sampledText;
        }

        for (var channel = 1; channel <= GeneratedAudioCodebookCount; channel++)
        {
            if (!provided[channel])
            {
                _ringCache[RingIndex(channel, targetPosition)] = sampledAudio[channel - 1];
            }
        }

        if (_offset <= 1)
        {
            _offset++;
            return null;
        }

        var assistantCodes = new long[AudioCodebookCount];
        for (var codebook = 0; codebook < AudioCodebookCount; codebook++)
        {
            var channel = codebook + 1;
            var outputPosition = (int)((_offset - 1 + Delays[channel]) % RingLength);
            assistantCodes[codebook] = _ringCache[RingIndex(channel, outputPosition)];
        }

        _offset++;
        return assistantCodes;
    }

    private (OrtValue Hidden, OrtValue TextLogits) RunTemporal(long[] temporalFrame)
    {
        var attentionMask = new long[checked((int)_temporalPosition + 1)];
        Array.Fill(attentionMask, 1L);
        var position = new[] { _temporalPosition };

        using var frameValue = OrtValue.CreateTensorValueFromMemory(temporalFrame, TemporalFrameShape);
        using var attentionValue = OrtValue.CreateTensorValueFromMemory(
            attentionMask,
            [1, attentionMask.LongLength]);
        using var positionValue = OrtValue.CreateTensorValueFromMemory(position, TemporalPositionShape);
        using var runOptions = new RunOptions();
        using var binding = _models.Temporal.CreateIoBinding();

        binding.BindInput("input_frame", frameValue);
        binding.BindInput("attention_mask", attentionValue);
        if (_models.TemporalUsesPositionIds)
        {
            binding.BindInput("position_ids", positionValue);
        }

        var inputCache = _temporalOutputs is null
            ? (IReadOnlyList<OrtValue>)_initialTemporalCache
            : _temporalOutputs.Skip(2).ToArray();
        var isFirstTemporalStep = _temporalOutputs is null;
        for (var layer = 0; layer < PersonaPlexModelManifest.TemporalLayerCount; layer++)
        {
            binding.BindInput($"past_key_values.{layer}.key", inputCache[layer * 2]);
            binding.BindInput($"past_key_values.{layer}.value", inputCache[(layer * 2) + 1]);
        }

        binding.BindOutputToDevice("hidden", OrtMemoryInfo.DefaultInstance);
        binding.BindOutputToDevice("text_logits", OrtMemoryInfo.DefaultInstance);
        for (var layer = 0; layer < PersonaPlexModelManifest.TemporalLayerCount; layer++)
        {
            binding.BindOutputToDevice($"present.{layer}.key", _models.CudaMemoryInfo);
            binding.BindOutputToDevice($"present.{layer}.value", _models.CudaMemoryInfo);
        }

        binding.SynchronizeBoundInputs();
        var nextOutputs = _models.Temporal.RunWithBoundResults(runOptions, binding);
        try
        {
            binding.SynchronizeBoundOutputs();
            if (nextOutputs.Count != 2 + (PersonaPlexModelManifest.TemporalLayerCount * 2))
            {
                throw new InvalidOperationException("PersonaPlex temporal graph returned an incompatible output set.");
            }

            if (isFirstTemporalStep)
            {
                using var cacheMemoryInfo = nextOutputs[2].GetTensorMemoryInfo();
                if (!string.Equals(cacheMemoryInfo.Name, "Cuda", StringComparison.Ordinal)
                    || cacheMemoryInfo.Id != _models.CudaMemoryInfo.Id)
                {
                    throw new InvalidOperationException(
                        "PersonaPlex temporal graph did not retain its KV cache on the configured CUDA device.");
                }
            }

            var previousOutputs = _temporalOutputs;
            _temporalOutputs = nextOutputs;
            nextOutputs = null!;
            previousOutputs?.Dispose();
            DisposeInitialTemporalCache();
            _temporalPosition++;

            return (_temporalOutputs[0], _temporalOutputs[1]);
        }
        finally
        {
            nextOutputs?.Dispose();
        }
    }

    private long[] RunDepformer(
        long textToken,
        OrtValue hidden,
        ReadOnlySpan<long> audioTarget,
        ReadOnlySpan<bool> audioProvided)
    {
        var initialCache = CreateEmptyDepformerCache();
        IDisposableReadOnlyCollection<OrtValue>? previousOutputs = null;
        var sampled = new long[GeneratedAudioCodebookCount];
        var previousToken = textToken;

        try
        {
            for (var substep = 0; substep < GeneratedAudioCodebookCount; substep++)
            {
                long[] previousTokenInput = [previousToken];
                long[] substepInput = [substep];
                using var previousTokenValue = OrtValue.CreateTensorValueFromMemory(
                    previousTokenInput,
                    DepformerPreviousTokenShape);
                using var substepValue = OrtValue.CreateTensorValueFromMemory(
                    substepInput,
                    ScalarShape);
                using var runOptions = new RunOptions();

                var inputNames = new string[3 + (PersonaPlexModelManifest.DepformerLayerCount * 2)];
                var inputValues = new OrtValue[inputNames.Length];
                inputNames[0] = "hidden";
                inputNames[1] = "prev_token";
                inputNames[2] = "substep_index";
                inputValues[0] = hidden;
                inputValues[1] = previousTokenValue;
                inputValues[2] = substepValue;

                var inputCache = previousOutputs is null
                    ? (IReadOnlyList<OrtValue>)initialCache
                    : previousOutputs.Skip(1).ToArray();
                for (var layer = 0; layer < PersonaPlexModelManifest.DepformerLayerCount; layer++)
                {
                    var nameIndex = 3 + (layer * 2);
                    inputNames[nameIndex] = $"past_key_values.{layer}.key";
                    inputNames[nameIndex + 1] = $"past_key_values.{layer}.value";
                    inputValues[nameIndex] = inputCache[layer * 2];
                    inputValues[nameIndex + 1] = inputCache[(layer * 2) + 1];
                }

                var outputs = _models.Depformer.Run(runOptions, inputNames, inputValues, DepformerOutputNames);
                try
                {
                    var sampledToken = SampleLogits(outputs[0], AudioTemperature, AudioTopK);
                    var oldOutputs = previousOutputs;
                    previousOutputs = outputs;
                    outputs = null!;
                    oldOutputs?.Dispose();
                    DisposeValues(initialCache);
                    initialCache = [];

                    previousToken = audioProvided[substep] ? audioTarget[substep] : sampledToken;
                    sampled[substep] = sampledToken;
                }
                finally
                {
                    outputs?.Dispose();
                }
            }

            return sampled;
        }
        finally
        {
            previousOutputs?.Dispose();
            DisposeValues(initialCache);
        }
    }

    private short[] DecodeNewestFrame(ReadOnlySpan<long> assistantCodes)
    {
        for (var codebook = 0; codebook < AudioCodebookCount; codebook++)
        {
            var rowStart = codebook * CodecWindowFrames;
            Array.Copy(_decoderWindow, rowStart + 1, _decoderWindow, rowStart, CodecWindowFrames - 1);
            _decoderWindow[rowStart + CodecWindowFrames - 1] = assistantCodes[codebook];
        }

        using var input = OrtValue.CreateTensorValueFromMemory(_decoderWindow, DecoderInputShape);
        using var runOptions = new RunOptions();
        using var outputs = _models.Decoder.Run(runOptions, DecoderInputNames, [input], DecoderOutputNames);
        var output = outputs[0];
        var shape = output.GetTensorTypeAndShape().Shape;
        if (shape.Length != 3 || shape[0] != 1 || shape[1] != 1 || shape[2] < FrameSampleCount)
        {
            throw new InvalidOperationException("PersonaPlex Mimi decoder returned an incompatible runtime shape.");
        }

        var waveform = output.GetTensorDataAsSpan<float>();
        var result = new short[FrameSampleCount];
        var newestFrame = waveform[^FrameSampleCount..];
        for (var sample = 0; sample < result.Length; sample++)
        {
            result[sample] = (short)Math.Clamp(
                (int)MathF.Round(newestFrame[sample] * 32768F),
                short.MinValue,
                short.MaxValue);
        }

        return result;
    }

    private int SampleLogits(OrtValue logits, float temperature, int topK)
    {
        if (_models.LanguageModelElementType == TensorElementType.Float)
        {
            return SampleLogits(logits.GetTensorDataAsSpan<float>(), temperature, topK);
        }

        var halfLogits = logits.GetTensorDataAsSpan<Half>();
        var rented = ArrayPool<float>.Shared.Rent(halfLogits.Length);
        try
        {
            var floatLogits = rented.AsSpan(0, halfLogits.Length);
            for (var index = 0; index < halfLogits.Length; index++)
            {
                floatLogits[index] = (float)halfLogits[index];
            }

            return SampleLogits(floatLogits, temperature, topK);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented, clearArray: true);
        }
    }

    private int SampleLogits(ReadOnlySpan<float> logits, float temperature, int topK)
    {
        var queue = new PriorityQueue<(int Token, float Logit), float>();
        for (var token = 0; token < logits.Length; token++)
        {
            var logit = logits[token];
            if (!float.IsFinite(logit))
            {
                continue;
            }

            queue.Enqueue((token, logit), logit);
            if (queue.Count > topK)
            {
                queue.Dequeue();
            }
        }

        if (queue.Count == 0)
        {
            throw new InvalidOperationException("PersonaPlex graph produced no finite logits.");
        }

        var candidates = new (int Token, float Logit)[queue.Count];
        var candidateIndex = 0;
        var maximum = float.NegativeInfinity;
        foreach (var candidate in queue.UnorderedItems)
        {
            candidates[candidateIndex++] = candidate.Element;
            maximum = Math.Max(maximum, candidate.Element.Logit);
        }

        var totalWeight = 0D;
        var weights = new double[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            weights[index] = Math.Exp((candidates[index].Logit - maximum) / temperature);
            totalWeight += weights[index];
        }

        var target = _random.NextDouble() * totalWeight;
        for (var index = 0; index < candidates.Length; index++)
        {
            target -= weights[index];
            if (target <= 0)
            {
                return candidates[index].Token;
            }
        }

        return candidates[^1].Token;
    }

    private void ResetCore()
    {
        DisposeTemporalState();
        Array.Clear(_encoderWindow);
        Array.Clear(_decoderWindow);
        Array.Fill(_ringCache, UngeneratedToken);
        Array.Clear(_provided);
        _offset = 0;
        _temporalPosition = 0;
        _initialTemporalCache = CreateEmptyTemporalCache();
    }

    private OrtValue[] CreateEmptyTemporalCache()
    {
        var values = new OrtValue[PersonaPlexModelManifest.TemporalLayerCount * 2];
        try
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = OrtValue.CreateAllocatedTensorValue(
                    _models.TemporalCudaAllocator,
                    _models.LanguageModelElementType,
                    TemporalEmptyCacheShape);
            }

            return values;
        }
        catch
        {
            DisposeValues(values);
            throw;
        }
    }

    private OrtValue[] CreateEmptyDepformerCache()
    {
        var values = new OrtValue[PersonaPlexModelManifest.DepformerLayerCount * 2];
        try
        {
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = _models.LanguageModelElementType == TensorElementType.Float
                    ? OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), DepformerEmptyCacheShape)
                    : OrtValue.CreateTensorValueFromMemory(Array.Empty<Half>(), DepformerEmptyCacheShape);
            }

            return values;
        }
        catch
        {
            DisposeValues(values);
            throw;
        }
    }

    private void DisposeTemporalState()
    {
        _temporalOutputs?.Dispose();
        _temporalOutputs = null;
        DisposeInitialTemporalCache();
    }

    private void DisposeInitialTemporalCache()
    {
        DisposeValues(_initialTemporalCache);
        _initialTemporalCache = [];
    }

    private static void DisposeValues(IEnumerable<OrtValue?> values)
    {
        foreach (var value in values)
        {
            value?.Dispose();
        }
    }

    private static int RingIndex(int channel, int position) => (channel * RingLength) + position;

    private static string[] CreateDepformerOutputNames()
    {
        var names = new string[1 + (PersonaPlexModelManifest.DepformerLayerCount * 2)];
        names[0] = "logits";
        for (var layer = 0; layer < PersonaPlexModelManifest.DepformerLayerCount; layer++)
        {
            names[1 + (layer * 2)] = $"present.{layer}.key";
            names[2 + (layer * 2)] = $"present.{layer}.value";
        }

        return names;
    }
}
