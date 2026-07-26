namespace DigitalBrain.Behaviors.Runtime.Artifacts;

using System.Buffers.Binary;
using System.Text;
using DigitalBrain.Behaviors.Artifacts;

internal static class CanonicalZip
{
    private const int LocalHeaderLength = 30;
    private const int CentralHeaderLength = 46;
    private const int EndOfCentralDirectoryLength = 22;
    private const ushort CanonicalVersion = 20;
    private const ushort CanonicalVersionMadeBy = 0x0014;
    private const ushort CanonicalDosDate = 0x0021;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyDictionary<string, Entry> Validate(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (bytes.Length < EndOfCentralDirectoryLength || bytes.Length > CanonicalArtifactWriter.MaximumSerializedBytes)
            {
                throw new BehaviorArtifactException("The behavior artifact ZIP envelope exceeds the permitted serialized size.");
            }

            var eocd = bytes.Length - EndOfCentralDirectoryLength;
            RequireSignature(bytes, eocd, 0x06054B50u);
            if (U16(bytes, eocd + 4) != 0 || U16(bytes, eocd + 6) != 0
                || U16(bytes, eocd + 8) != U16(bytes, eocd + 10)
                || U16(bytes, eocd + 20) != 0)
            {
                throw new BehaviorArtifactException("The behavior artifact must be a single-disk ZIP without comments.");
            }

            var count = U16(bytes, eocd + 10);
            var centralSize = U32(bytes, eocd + 12);
            var centralOffset = U32(bytes, eocd + 16);
            if (count > CanonicalArtifactWriter.MaximumEntries || centralOffset > int.MaxValue || centralSize > int.MaxValue
                || checked((long)centralOffset + centralSize) != eocd)
            {
                throw new BehaviorArtifactException("The behavior artifact has an invalid or non-canonical central directory.");
            }

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var cursor = (int)centralOffset;
            var localEnd = 0;
            string? previousName = null;
            long total = 0;
            for (var index = 0; index < count; index++)
            {
                RequireRange(bytes, cursor, CentralHeaderLength);
                RequireSignature(bytes, cursor, 0x02014B50u);
                if (U16(bytes, cursor + 4) != CanonicalVersionMadeBy || U16(bytes, cursor + 6) != CanonicalVersion
                    || U16(bytes, cursor + 8) != 0 || U16(bytes, cursor + 10) != 0
                    || U16(bytes, cursor + 12) != 0 || U16(bytes, cursor + 14) != CanonicalDosDate
                    || U16(bytes, cursor + 30) != 0 || U16(bytes, cursor + 32) != 0
                    || U16(bytes, cursor + 34) != 0 || U16(bytes, cursor + 36) != 0
                    || U32(bytes, cursor + 38) != 0)
                {
                    throw new BehaviorArtifactException("The behavior artifact contains non-canonical ZIP metadata.");
                }

                var compressed = U32(bytes, cursor + 20);
                var uncompressed = U32(bytes, cursor + 24);
                var nameLength = U16(bytes, cursor + 28);
                var localOffset = U32(bytes, cursor + 42);
                if (compressed != uncompressed || uncompressed > CanonicalArtifactWriter.MaximumEntryBytes
                    || localOffset > int.MaxValue)
                {
                    throw new BehaviorArtifactException("The behavior artifact contains compressed or oversized ZIP data.");
                }

                RequireRange(bytes, cursor + CentralHeaderLength, nameLength);
                var name = DecodeName(bytes.Slice(cursor + CentralHeaderLength, nameLength));
                if (string.CompareOrdinal(previousName, name) >= 0 || !entries.TryAdd(name, new Entry(name, (int)uncompressed, (int)localOffset)))
                {
                    throw new BehaviorArtifactException("The behavior artifact central directory is not strictly ordered.");
                }

                ValidateLocalEntry(bytes, (int)localOffset, name, (int)uncompressed, ref localEnd);
                previousName = name;
                total = checked(total + uncompressed);
                if (total > CanonicalArtifactWriter.MaximumExpandedBytes)
                {
                    throw new BehaviorArtifactException("The behavior artifact expands beyond the permitted limit.");
                }

                cursor = checked(cursor + CentralHeaderLength + nameLength);
            }

            if (cursor != eocd || localEnd != centralOffset)
            {
                throw new BehaviorArtifactException("The behavior artifact ZIP records are not adjacent.");
            }

            return entries;
        }
        catch (BehaviorArtifactException)
        {
            throw;
        }
        catch (DecoderFallbackException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact contains a non-UTF-8 ZIP entry name.", exception);
        }
        catch (OverflowException exception)
        {
            throw new BehaviorArtifactException("The behavior artifact ZIP metadata overflowed a supported bound.", exception);
        }
    }

    internal static void NormalizeWriterMetadata(Span<byte> bytes)
    {
        var eocd = bytes.Length - EndOfCentralDirectoryLength;
        var cursor = checked((int)U32(bytes, eocd + 16));
        var count = U16(bytes, eocd + 10);
        for (var index = 0; index < count; index++)
        {
            RequireSignature(bytes, cursor, 0x02014B50u);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(cursor + 4, 2), CanonicalVersionMadeBy);
            bytes.Slice(cursor + 36, 2).Clear();
            bytes.Slice(cursor + 38, 4).Clear();
            cursor = checked(cursor + CentralHeaderLength + U16(bytes, cursor + 28));
        }
    }

    private static void ValidateLocalEntry(ReadOnlySpan<byte> bytes, int offset, string centralName, int length, ref int localEnd)
    {
        if (offset != localEnd)
        {
            throw new BehaviorArtifactException("The behavior artifact contains a ZIP prefix or gap.");
        }

        RequireRange(bytes, offset, LocalHeaderLength);
        RequireSignature(bytes, offset, 0x04034B50u);
        if (U16(bytes, offset + 4) != CanonicalVersion || U16(bytes, offset + 6) != 0 || U16(bytes, offset + 8) != 0
            || U16(bytes, offset + 10) != 0 || U16(bytes, offset + 12) != CanonicalDosDate || U32(bytes, offset + 18) != (uint)length
            || U32(bytes, offset + 22) != (uint)length || U16(bytes, offset + 28) != 0)
        {
            throw new BehaviorArtifactException("The behavior artifact contains non-canonical local ZIP metadata.");
        }

        var nameLength = U16(bytes, offset + 26);
        RequireRange(bytes, offset + LocalHeaderLength, nameLength);
        if (!string.Equals(centralName, DecodeName(bytes.Slice(offset + LocalHeaderLength, nameLength)), StringComparison.Ordinal))
        {
            throw new BehaviorArtifactException("The behavior artifact local and central ZIP names differ.");
        }

        localEnd = checked(offset + LocalHeaderLength + nameLength + length);
        RequireRange(bytes, offset, localEnd - offset);
    }

    private static string DecodeName(ReadOnlySpan<byte> bytes) => StrictUtf8.GetString(bytes);
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
    private static void RequireSignature(ReadOnlySpan<byte> bytes, int offset, uint signature)
    {
        RequireRange(bytes, offset, 4);
        if (U32(bytes, offset) != signature)
        {
            throw new BehaviorArtifactException("The behavior artifact has an invalid ZIP record signature.");
        }
    }
    private static void RequireRange(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new BehaviorArtifactException("The behavior artifact ZIP metadata exceeds the envelope bounds.");
        }
    }

    internal sealed record Entry(string Name, int Length, int LocalHeaderOffset);
}
