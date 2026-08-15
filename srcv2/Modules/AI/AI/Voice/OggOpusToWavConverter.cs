using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace DigitalBrain.AI;

// Telegram-class Ogg Opus → PCM WAV (IAW AudioConverter port).
public sealed class OggOpusToWavConverter : IAudioConverter
{
    private const int SampleRate = 48000;
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BytesPerSample = 2;

    public string ConvertToWav(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var outputPath = Path.Combine(Path.GetTempPath(), $"db_voice_{Guid.NewGuid():N}.wav");

        using var fileIn = File.OpenRead(inputPath);
        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var oggIn = new OpusOggReadStream(decoder, fileIn);
        using var wavWriter = new WaveFileWriter(outputPath, new WaveFormat(SampleRate, BitsPerSample, Channels));

        while (oggIn.HasNextPacket)
        {
            var samples = oggIn.DecodeNextPacket();
            if (samples is not { Length: > 0 })
            {
                continue;
            }

            var bytes = new byte[samples.Length * BytesPerSample];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            wavWriter.Write(bytes, 0, bytes.Length);
        }

        return outputPath;
    }
}
