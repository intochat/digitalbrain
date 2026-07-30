namespace DigitalBrain.Behaviors.Tests;

using System;
using System.Buffers.Binary;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using Xunit;

public sealed partial class CanonicalArtifacts
{
    [Fact(DisplayName = "Reader rejects bytes after a complete ZIP envelope")]
    public void ReaderRejectsTrailingBytes()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var bytes = artifact.Bytes.Concat(new byte[] { 0x00, 0x01, 0x02 }).ToArray();

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects forged end records and central compression metadata before extraction")]
    public void ReaderRejectsForgedOrCompressedRawZipMetadata()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var forged = artifact.Bytes.Concat(new byte[22]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(forged.AsSpan(forged.Length - 22, 4), 0x06054B50u);
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(forged));

        var compressed = artifact.Bytes.ToArray();
        var central = FindSignature(compressed, 0x02014B50u);
        BinaryPrimitives.WriteUInt16LittleEndian(compressed.AsSpan(central + 10, 2), 8);
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(compressed));
    }

    [Fact(DisplayName = "Reader rejects each non-canonical raw ZIP invariant")]
    public void ReaderRejectsRawZipInvariantMutations()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false)).Bytes;
        var central = FindSignature(artifact, 0x02014B50u);
        var secondCentral = FindSignature(artifact, 0x02014B50u, central + 1);
        var local = FindSignature(artifact, 0x04034B50u);

        var mutations = new Action<byte[]>[]
        {
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 36, 2), 1),
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 38, 4), 0xA1B20000u),
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 8, 2), 0x0008),
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16, 4), 1),
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 12, 2), 1),
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 30, 2), 1),
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 32, 2), 1),
            bytes => bytes[0] = 0,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(secondCentral + 42, 4), 1),
            bytes => bytes[local + 30] = (byte)'X',
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 22, 4), 1),
            bytes =>
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 20, 4), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24, 4), 1);
            },
            bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(central + 10, 2), 8),
            bytes => bytes[central + 46] = 0xFF,
        };

        foreach (var mutate in mutations)
        {
            var bytes = artifact.ToArray();
            mutate(bytes);
            Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
        }

        var malformedText = MutateStoredEntryBytes(artifact, "Behavior.cs", bytes => bytes[0] = 0xFF);
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(malformedText));
    }

    [Fact(DisplayName = "Reader contains hostile manifest construction failures as artifact exceptions")]
    public void ReaderContainsHostileManifestFailures()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false)).Bytes;
        var mutations = new[]
        {
            ("\"Behavior\"", "\"missingx\""),
            ("com.digitalbrain.start-ui", "INVALID.BEHAVIOR........."),
            ("\"EntryPoints\"", "\"missingData\""),
            ("\"CapabilityGrants\"", "\"missingCapabilit\""),
            ("\"db.shell\"", "null      "),
        };

        foreach (var (original, replacement) in mutations)
        {
            var bytes = ReplaceStoredEntryText(artifact, "manifest.json", original, replacement);
            Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
        }
    }
}
