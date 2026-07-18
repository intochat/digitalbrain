using DigitalBrain.Runtime.Ai;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Models;

public sealed record LargeV3Turbo : IVoiceModel
{
    public static string Id => "whisper-large-v3-turbo";
    public static string DisplayName => "Whisper Large V3 Turbo";
    public static string Icon => "whisper";
    public static string ModelFileName => "ggml-large-v3-turbo.bin";
    // Hash from whisper.cpp release manifest; verify against
    // https://huggingface.co/ggerganov/whisper.cpp/tree/main when bumping.
    public static string? ModelFileSha256 => "1fc70f774d38eb169993ac391eea4f1325a3a6116da5c3a93175b2b2d8a1ff19";
}

public sealed record Small : IVoiceModel
{
    public static string Id => "whisper-small";
    public static string DisplayName => "Whisper Small";
    public static string Icon => "whisper";
    public static string ModelFileName => "ggml-small.bin";
    public static string? ModelFileSha256 => null;
}

public sealed record Tiny : IVoiceModel
{
    public static string Id => "whisper-tiny";
    public static string DisplayName => "Whisper Tiny";
    public static string Icon => "whisper";
    public static string ModelFileName => "ggml-tiny.bin";
    public static string? ModelFileSha256 => null;
}
