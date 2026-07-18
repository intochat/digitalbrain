using System.Security.Cryptography;
using Microsoft.Extensions.AI;
using Whisper.net;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Voice;

internal static class WhisperSpeechToTextClientFactory
{
    public static ISpeechToTextClient Create(string modelFileName, string? expectedSha256)
    {
        var modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DigitalBrain", "whisper-models");
        Directory.CreateDirectory(modelDir);

        var modelPath = Path.Combine(modelDir, modelFileName);

        if (!File.Exists(modelPath))
            DownloadModel(modelFileName, modelPath);

        if (expectedSha256 is not null)
            VerifyChecksum(modelPath, modelFileName, expectedSha256);

        // Whisper.net 1.9 ships WhisperSpeechToTextClient implementing
        // ISpeechToTextClient directly. The factory chooses the best runtime
        // (CUDA on Windows, CoreML on macOS, Vulkan on Linux, CPU fallback)
        // based on which Whisper.net.Runtime.* package was pulled.
        return new WhisperSpeechToTextClient(modelPath);
    }

    static void DownloadModel(string modelFileName, string modelPath)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelFileName}";
        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var src = response.Content.ReadAsStream();
        using var dst = File.Create(modelPath);
        src.CopyTo(dst);
    }

    static void VerifyChecksum(string modelPath, string modelFileName, string expectedSha256)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(modelPath);
        var actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Whisper model checksum mismatch for {modelFileName}: " +
                $"expected {expectedSha256}, got {actual}.");
    }
}
