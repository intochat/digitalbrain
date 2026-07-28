namespace DigitalBrain.Behaviors.Tests;

using System;
using System.Buffers.Binary;
using System.IO.Compression;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using Xunit;

public sealed partial class CanonicalArtifacts
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
}
