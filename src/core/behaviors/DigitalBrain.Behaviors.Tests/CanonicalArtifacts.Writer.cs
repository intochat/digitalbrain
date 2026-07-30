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
    [Fact(DisplayName = "Canonical artifact bytes and digest do not depend on evidence or scenario input ordering")]
    public void SameEvidenceProducesSameDigest()
    {
        var first = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var second = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: true));

        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact(DisplayName = "Canonical artifact writes exactly Behavior.cs and Behavior.feature as authored files")]
    public void WriterUsesTheExactEnvelopeLayout()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));

        using var archive = new ZipArchive(new MemoryStream(artifact.Bytes), ZipArchiveMode.Read);
        Assert.Equal(
            [
                "Behavior.cs",
                "Behavior.feature",
                "artifact/Behavior.deps.json",
                "artifact/Behavior.dll",
                "dependencies/packages.lock.json",
                "evidence/admission.json",
                "evidence/bdd.json",
                "evidence/compiler.json",
                "manifest.json",
            ],
            archive.Entries.Select(entry => entry.FullName));
        Assert.All(archive.Entries, entry =>
        {
            Assert.Equal(entry.Length, entry.CompressedLength);
            Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 0), entry.LastWriteTime.DateTime);
        });
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("features/", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName is "program.cs");
    }

    [Fact(DisplayName = "Writer records deterministic compiler policy and generated overview in the signed manifest")]
    public void WriterRecordsCompilerPolicyAndOverview()
    {
        var artifact = CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false));
        var envelope = CanonicalArtifactReader.Read(artifact.Bytes);

        Assert.Equal("11.0.100-preview.6", envelope.Manifest.CompilerPolicy.SdkVersion);
        Assert.Equal("5.6.0", envelope.Manifest.CompilerPolicy.RoslynVersion);
        Assert.Equal("Preview", envelope.Manifest.CompilerPolicy.LanguageVersion);
        Assert.Equal("contract-only-v1", envelope.Manifest.CompilerPolicy.PolicyId);
        Assert.Equal("Start UI opens the first scene for alpha and zulu paths.", envelope.Manifest.Overview);
        Assert.Equal(2, envelope.Manifest.Scenarios.Count);
        Assert.Equal("com.digitalbrain.start-ui", envelope.Manifest.EntryPoints.Contract.BehaviorContractId);
        Assert.Contains("sdk", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("roslyn", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("languageVersion", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("policy", envelope.CompilerEvidenceJson, StringComparison.Ordinal);
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

    [Fact(DisplayName = "Writer rejects secret-like generated overview or evidence content")]
    public void WriterRejectsSecretLikeGeneratedContent()
    {
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false) with
        {
            Manifest = CreateEnvelope(reverseOrder: false).Manifest with
            {
                Overview = "Connect using password: hunter2",
            },
        }));

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactWriter.Write(CreateEnvelope(reverseOrder: false) with
        {
            CompilerEvidenceJson = "{\"detail\":\"api_key: abc\",\"sdk\":\"11\"}",
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

    [Fact(DisplayName = "Contract payload and result schemas are canonical manifest evidence and alter the artifact identity")]
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
