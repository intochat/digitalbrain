namespace DigitalBrain;

/// <summary>A deployed synapse source as stored: its source, its build and what it declares.</summary>
/// <param name="SourceKey">The source key.</param>
/// <param name="Files">The C# files, keyed by path.</param>
/// <param name="Icon">The package icon bytes, if any.</param>
/// <param name="Hash">The build hash.</param>
/// <param name="AssemblyName">The compiled assembly name.</param>
/// <param name="Assembly">The compiled assembly bytes.</param>
/// <param name="Symbols">The portable PDB bytes.</param>
/// <param name="Aliases">The state aliases the source declares.</param>
/// <param name="DeployedAt">When it went live.</param>
public sealed record SourceRecord(
    string SourceKey,
    IReadOnlyDictionary<string, string> Files,
    byte[]? Icon,
    string Hash,
    string AssemblyName,
    byte[] Assembly,
    byte[] Symbols,
    IReadOnlyList<string> Aliases,
    DateTimeOffset DeployedAt);

/// <summary>Where a synapse host keeps the live sources of each brain.</summary>
public interface ISourceStore
{
    /// <summary>The record of <paramref name="sourceKey"/>, or null when none is stored.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct);

    /// <summary>Stores <paramref name="record"/>, replacing the record of the same source key.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="record">The record.</param>
    /// <param name="ct">Cancellation.</param>
    Task Put(BrainId brain, SourceRecord record, CancellationToken ct);

    /// <summary>Removes the record of <paramref name="sourceKey"/>; an unknown key is ignored.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="sourceKey">The source key.</param>
    /// <param name="ct">Cancellation.</param>
    Task Remove(BrainId brain, string sourceKey, CancellationToken ct);

    /// <summary>Every record of <paramref name="brain"/>.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct);
}
