namespace DigitalBrain.AI.PersonaPlex;

public sealed record PersonaPlexAudioFrame
{
    private PersonaPlexAudioFrame(long sequence, ReadOnlyMemory<short> pcm16)
    {
        Sequence = sequence;
        Pcm16 = pcm16;
    }

    public long Sequence { get; }

    public ReadOnlyMemory<short> Pcm16 { get; }

    public static PersonaPlexAudioFrame Create(long sequence, ReadOnlyMemory<short> pcm16)
        => pcm16.Length == 1920
            ? new PersonaPlexAudioFrame(sequence, pcm16)
            : throw new ArgumentException("PersonaPlex frames require exactly 1920 PCM16 samples.", nameof(pcm16));
}
