namespace DigitalBrain.State;

/// <summary>
/// Marks a persisted state type as customer data that the portable brain export may copy.
/// Export is default-deny: an unmarked state type is never exported, and identity or secret
/// state names stay denied even when the type is marked.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ExportableStateAttribute : Attribute;

/// <summary>A stored record: codec bytes and the store's concurrency tag.</summary>
/// <param name="Record">The codec bytes.</param>
/// <param name="ETag">The concurrency tag.</param>
public sealed record StoredRecord(ReadOnlyMemory<byte> Record, long ETag);

/// <summary>The outcome of a store write.</summary>
/// <param name="ETag">The new concurrency tag.</param>
/// <param name="Sequence">The last journal sequence, or null when the store journals nothing.</param>
public sealed record WriteResult(long ETag, long? Sequence);

/// <summary>The seam that keeps stores runtime-free: the runtime supplies the codec.</summary>
public interface IStateCodec
{
    /// <summary>The codec id written with each record.</summary>
    byte CodecId { get; }

    /// <summary>Encodes a value.</summary>
    byte[] Serialize<T>(T value);

    /// <summary>Decodes a value.</summary>
    T Deserialize<T>(ReadOnlySpan<byte> bytes);
}
