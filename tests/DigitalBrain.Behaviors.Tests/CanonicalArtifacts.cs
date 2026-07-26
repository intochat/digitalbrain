namespace DigitalBrain.Behaviors.Tests;

using System;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using Xunit;

public sealed class CanonicalArtifacts
{
    [Fact(DisplayName = "Canonical artifact bytes and digest do not depend on evidence or feature input ordering")]
    public void SameEvidenceProducesSameDigest()
    {
        var first = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var second = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: true));

        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.Bytes, second.Bytes);
        Assert.Equal("694ea25c6cb996e4c9b16cfe19e5692c58c7015b6fa12c9651a4d6f8ebb8aaba", first.Digest.Value);
    }

    [Fact(DisplayName = "Canonical artifact writes the closed ordered envelope with stored entries and fixed timestamps")]
    public void WriterUsesTheExactEnvelopeLayout()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));

        using var archive = new ZipArchive(new MemoryStream(artifact.Bytes), ZipArchiveMode.Read);
        Assert.Equal(
            [
                "artifact/Behavior.deps.json",
                "artifact/Behavior.dll",
                "dependencies/packages.lock.json",
                "evidence/admission.json",
                "evidence/bdd.json",
                "evidence/compiler.json",
                "features/alpha.feature",
                "features/zulu.feature",
                "manifest.json",
                "program.cs",
            ],
            archive.Entries.Select(entry => entry.FullName));
        Assert.All(archive.Entries, entry =>
        {
            Assert.Equal(entry.Length, entry.CompressedLength);
            Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime);
        });
    }

    [Fact(DisplayName = "Writer normalizes central-directory host and external-attribute metadata")]
    public void WriterNormalizesCentralDirectoryMetadata()
    {
        var bytes = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false)).Bytes;
        var central = FindSignature(bytes, 0x02014B50u);

        Assert.Equal((ushort)0x0014, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 4, 2)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(central + 36, 2)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central + 38, 4)));
    }

    [Fact(DisplayName = "Reader round-trips every required artifact payload and canonicalizes JSON evidence")]
    public void ReaderRoundTripsAValidEnvelope()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var envelope = CanonicalArtifactReader.Read(artifact.Bytes);

        Assert.Equal("com.digitalbrain.start-ui", envelope.Manifest.Behavior.Value);
        var intent = Assert.Single(envelope.Manifest.EntryPoints.IntentSchemas);
        Assert.Equal("com.digitalbrain.start-ui", intent.SchemaId);
        Assert.Equal(1, intent.SchemaVersion);
        Assert.Equal("{\"properties\":{\"scene\":{\"type\":\"string\"}},\"type\":\"object\"}", intent.RequestSchemaJson);
        Assert.Equal("{\"properties\":{\"opened\":{\"type\":\"boolean\"}},\"type\":\"object\"}", intent.ResultSchemaJson);
        Assert.Equal("public sealed class StartUi { }\n", envelope.ProgramSource);
        Assert.Equal("{\"libraries\":{},\"version\":1}", envelope.PackageLockJson);
        Assert.Equal([0, 1, 2, 3], envelope.BehaviorAssembly.ToArray());
        Assert.Equal("{\"runtimeTarget\":{\"name\":\"net10.0\"}}", envelope.BehaviorDependenciesJson);
        Assert.Equal("Feature: alpha\n", envelope.Features["alpha"]);
        Assert.Equal("Feature: zulu\n", envelope.Features["zulu"]);
        Assert.Equal("{\"diagnostics\":[],\"sdk\":\"10.0.302\"}", envelope.CompilerEvidenceJson);
        Assert.Equal("{\"policy\":\"v1\",\"result\":\"accepted\"}", envelope.AdmissionEvidenceJson);
        Assert.Equal("{\"passed\":true,\"scenarios\":1}", envelope.BddEvidenceJson);
        Assert.Equal(artifact.Digest, BehaviorArtifactDigest.Compute(artifact.Bytes));
    }

    [Theory(DisplayName = "Reader rejects unsafe paths and case-insensitive name collisions")]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/absolute.dll")]
    [InlineData("ARTIFACT/behavior.dll")]
    public void ReaderRejectsUnsafeOrCaseCollidingEntryNames(string entry)
        => Assert.Throws<BehaviorArtifactException>(() => ReadWithExtraEntry(entry));

    [Fact(DisplayName = "Reader rejects symbolic-link entries before extracting any artifact data")]
    public void ReaderRejectsLinkEntries()
    {
        var bytes = CreateZip((archive, _) =>
        {
            var link = archive.CreateEntry("features/link.feature", CompressionLevel.NoCompression);
            link.ExternalAttributes = unchecked((int)0xA0000000);
            WriteText(link, "artifact/Behavior.dll");
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects exact duplicate artifact entry names")]
    public void ReaderRejectsDuplicateEntries()
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry("program.cs", CompressionLevel.NoCompression), "duplicate"));

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects names outside the closed envelope")]
    public void ReaderRejectsUnknownEntries()
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry("surprise.txt", CompressionLevel.NoCompression), "no"));

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader requires every non-feature envelope entry")]
    public void ReaderRejectsMissingRequiredEntry()
    {
        var bytes = CreateRequiredZip(skip: "evidence/bdd.json");

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects more than 128 entries before reading their contents")]
    public void ReaderRejectsTooManyEntries()
    {
        var bytes = CreateZip((archive, _) =>
        {
            for (var index = 0; index < 121; index++)
            {
                WriteText(archive.CreateEntry($"features/f{index:D3}.feature", CompressionLevel.NoCompression), "Feature: bounded\n");
            }
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects an entry whose declared expansion exceeds 16 MiB")]
    public void ReaderRejectsOversizedEntry()
    {
        var bytes = CreateZip((archive, _) =>
        {
            archive.GetEntry("artifact/Behavior.dll")!.Delete();
            WriteBytes(
                archive.CreateEntry("artifact/Behavior.dll", CompressionLevel.NoCompression),
                new byte[(16 * 1024 * 1024) + 1]);
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects a total declared expansion over 64 MiB")]
    public void ReaderRejectsOversizedExpansion()
    {
        var bytes = CreateZip((archive, _) =>
        {
            for (var index = 0; index < 4; index++)
            {
                WriteBytes(
                    archive.CreateEntry($"features/f{index}.feature", CompressionLevel.NoCompression),
                    new byte[16 * 1024 * 1024]);
            }

            WriteBytes(archive.CreateEntry("features/f4.feature", CompressionLevel.NoCompression), [0]);
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

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

        var malformedText = MutateStoredEntryBytes(artifact, "program.cs", bytes => bytes[0] = 0xFF);
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

    [Theory(DisplayName = "Writer rejects non-portable feature filenames")]
    [InlineData("CON")]
    [InlineData("prn.feature")]
    [InlineData("LPT9")]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad.")]
    [InlineData("bad ")]
    [InlineData("-bad")]
    [InlineData("bad-")]
    [InlineData("bad\u0001name")]
    public void WriterRejectsNonPortableFeatureNames(string featureName)
        => Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false) with
        {
            Features = new Dictionary<string, string> { [featureName] = "Feature: invalid\n" },
        }));

    [Fact(DisplayName = "Writer bounds portable feature filenames")]
    public void WriterBoundsPortableFeatureNames()
    {
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false) with
        {
            Features = new Dictionary<string, string> { [new string('a', 129)] = "Feature: invalid\n" },
        }));
    }

    [Fact(DisplayName = "Artifact digest accepts only lowercase SHA-256 and exposes the exact revision value")]
    public void ArtifactDigestIsCanonicalLowercaseSha256()
    {
        const string digest = "fb1ea2ac934e969b05753dc5a9c21a2ad831b72fbca5164ec19ed271b8268c3d";

        Assert.Equal(digest, new BehaviorArtifactDigest(digest).Value);
        Assert.Throws<FormatException>(() => new BehaviorArtifactDigest(digest.ToUpperInvariant()));
        Assert.Throws<FormatException>(() => new BehaviorArtifactDigest(digest[..63]));
    }

    [Fact(DisplayName = "Intent request and result schemas are canonical manifest evidence and alter the artifact identity")]
    public void IntentSchemasAreCanonicalAndHashed()
    {
        var canonical = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var reordered = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: true));
        var changedRequest = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false, requestSchema: "{\"type\":\"array\"}"));
        var changedResult = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false, resultSchema: "{\"type\":\"array\"}"));

        Assert.Equal(canonical.Bytes, reordered.Bytes);
        Assert.NotEqual(canonical.Digest, changedRequest.Digest);
        Assert.NotEqual(canonical.Digest, changedResult.Digest);
    }

    [Fact(DisplayName = "Writer rejects duplicate nested JSON members and incomplete manifest graphs")]
    public void WriterRejectsAmbiguousSchemaJsonAndInvalidManifestGraphs()
    {
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(
            CreateEnvelope(reverseOrder: false, requestSchema: "{\"nested\":{\"value\":1,\"value\":2}}")));

        var invalid = CreateEnvelope(reverseOrder: false) with
        {
            Manifest = CreateEnvelope(reverseOrder: false).Manifest with
            {
                ResourceLimits = new BehaviorResourceLimits(0, 1, 1),
            },
        };
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(invalid));
    }

    private static BehaviorArtifactEnvelope CreateEnvelope(
        bool reverseOrder,
        string requestSchema = "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"}}}",
        string resultSchema = "{\"type\":\"object\",\"properties\":{\"opened\":{\"type\":\"boolean\"}}}")
    {
        IReadOnlyDictionary<string, string> features = reverseOrder
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["zulu"] = "Feature: zulu\n", ["alpha"] = "Feature: alpha\n" }
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["alpha"] = "Feature: alpha\n", ["zulu"] = "Feature: zulu\n" };

        return new BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.start-ui"),
                "Start UI",
                "Open the first scene.",
                reverseOrder
                    ? new BehaviorEntryPoints(
                        ["db.ready", "db.activated"],
                        [new BehaviorIntentSchema(
                            "com.digitalbrain.start-ui",
                            1,
                            requestSchema == "{\"type\":\"object\",\"properties\":{\"scene\":{\"type\":\"string\"}}}"
                                ? "{\"properties\":{\"scene\":{\"type\":\"string\"}},\"type\":\"object\"}"
                                : requestSchema,
                            resultSchema == "{\"type\":\"object\",\"properties\":{\"opened\":{\"type\":\"boolean\"}}}"
                                ? "{\"properties\":{\"opened\":{\"type\":\"boolean\"}},\"type\":\"object\"}"
                                : resultSchema)])
                    : new BehaviorEntryPoints(
                        ["db.activated", "db.ready"],
                        [new BehaviorIntentSchema("com.digitalbrain.start-ui", 1, requestSchema, resultSchema)]),
                reverseOrder
                    ? [new BehaviorCapabilityGrant("db.time", "schedule", "clock"), new BehaviorCapabilityGrant("db.shell", "open", "desk")]
                    : [new BehaviorCapabilityGrant("db.shell", "open", "desk"), new BehaviorCapabilityGrant("db.time", "schedule", "clock")],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            "public sealed class StartUi { }\n",
            reverseOrder ? "{\"version\":1,\"libraries\":{}}" : "{\"libraries\":{},\"version\":1}",
            new byte[] { 0, 1, 2, 3 },
            "{\"runtimeTarget\":{\"name\":\"net10.0\"}}",
            features,
            reverseOrder ? "{\"sdk\":\"10.0.302\",\"diagnostics\":[]}" : "{\"diagnostics\":[],\"sdk\":\"10.0.302\"}",
            "{\"result\":\"accepted\",\"policy\":\"v1\"}",
            "{\"scenarios\":1,\"passed\":true}");
    }

    private static void ReadWithExtraEntry(string name)
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry(name, CompressionLevel.NoCompression), "unsafe"));
        _ = CanonicalArtifactReader.Read(bytes);
    }

    private static byte[] CreateZip(Action<ZipArchive, byte[]> mutation)
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        using var stream = new MemoryStream();
        stream.Write(artifact.Bytes);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            mutation(archive, artifact.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] CreateRequiredZip(string? skip = null)
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        using var source = new ZipArchive(new MemoryStream(artifact.Bytes), ZipArchiveMode.Read);
        using var stream = new MemoryStream();
        using (var destination = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries.Where(entry => entry.FullName != skip))
            {
                var replacement = destination.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                replacement.LastWriteTime = entry.LastWriteTime;
                using var input = entry.Open();
                using var output = replacement.Open();
                input.CopyTo(output);
            }
        }

        return stream.ToArray();
    }

    private static void WriteText(ZipArchiveEntry entry, string value)
        => WriteBytes(entry, Encoding.UTF8.GetBytes(value));

    private static void WriteBytes(ZipArchiveEntry entry, byte[] value)
    {
        using var stream = entry.Open();
        stream.Write(value);
    }

    private static byte[] ReplaceStoredEntryText(byte[] artifact, string name, string original, string replacement)
    {
        Assert.Equal(original.Length, replacement.Length);
        var text = Encoding.UTF8.GetString(GetStoredEntryBytes(artifact, name));
        var index = text.IndexOf(original, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Could not find '{original}' in {name}.");
        text = string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + replacement.Length));
        return MutateStoredEntryBytes(artifact, name, bytes => Encoding.UTF8.GetBytes(text).CopyTo(bytes, 0));
    }

    private static byte[] MutateStoredEntryBytes(byte[] artifact, string name, Action<byte[]> mutate)
    {
        var bytes = artifact.ToArray();
        var local = FindEntryLocalHeader(bytes, name);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(local + 26, 2));
        var dataOffset = local + 30 + nameLength;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(local + 22, 4));
        var content = bytes.AsSpan(dataOffset, checked((int)length)).ToArray();
        mutate(content);
        content.CopyTo(bytes, dataOffset);

        var crc = ComputeCrc32(content);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 14, 4), crc);
        var central = FindCentralEntry(bytes, name);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16, 4), crc);
        return bytes;
    }

    private static byte[] GetStoredEntryBytes(byte[] artifact, string name)
    {
        var local = FindEntryLocalHeader(artifact, name);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(artifact.AsSpan(local + 26, 2));
        var dataOffset = local + 30 + nameLength;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(artifact.AsSpan(local + 22, 4));
        return artifact.AsSpan(dataOffset, checked((int)length)).ToArray();
    }

    private static int FindEntryLocalHeader(byte[] bytes, string name)
    {
        for (var offset = 0; offset < bytes.Length;)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != 0x04034B50u)
            {
                break;
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 26, 2));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 22, 4));
            if (Encoding.UTF8.GetString(bytes, offset + 30, nameLength) == name)
            {
                return offset;
            }

            offset += checked(30 + nameLength + (int)length);
        }

        throw new InvalidOperationException($"ZIP entry '{name}' was not found.");
    }

    private static int FindCentralEntry(byte[] bytes, string name)
    {
        for (var offset = FindSignature(bytes, 0x02014B50u); offset >= 0;)
        {
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
            if (Encoding.UTF8.GetString(bytes, offset + 46, nameLength) == name)
            {
                return offset;
            }

            offset = FindSignature(bytes, 0x02014B50u, offset + 1);
        }

        throw new InvalidOperationException($"Central ZIP entry '{name}' was not found.");
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
            }
        }

        return ~crc;
    }

    private static int FindSignature(byte[] bytes, uint signature, int start = 0)
    {
        for (var index = start; index <= bytes.Length - 4; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) == signature)
            {
                return index;
            }
        }

        throw new InvalidOperationException("ZIP signature was not found.");
    }
}
