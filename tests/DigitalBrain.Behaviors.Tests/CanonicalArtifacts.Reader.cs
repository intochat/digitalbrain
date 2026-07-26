namespace DigitalBrain.Behaviors.Tests;

using System.IO.Compression;
using System.Text;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using Xunit;

public sealed partial class CanonicalArtifacts
{
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
}
