namespace DigitalBrain;

/// <summary>Names a journaled signal as the cause of another: its publisher and that publisher's sequence.</summary>
public static class Causation
{
    /// <summary>The causation id of the signal <paramref name="publisher"/> journaled at <paramref name="sequence"/>.</summary>
    public static string Address(NeuronId publisher, long sequence) => $"{publisher}:{sequence}";
}
