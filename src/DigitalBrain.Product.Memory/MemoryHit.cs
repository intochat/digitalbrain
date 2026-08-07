namespace DigitalBrain.Product.Memory;

public sealed record MemoryHit
{
    public MemoryHit(MemoryEntry entry, double score)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (double.IsNaN(score) || double.IsInfinity(score) || score < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "A memory hit score must be finite and non-negative.");
        }

        Entry = entry;
        Score = score;
    }

    public MemoryEntry Entry { get; }

    public double Score { get; }
}
