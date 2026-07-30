namespace DigitalBrain.Behaviors.Tests;

using System.IO.Compression;
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
        var contract = envelope.Manifest.EntryPoints.Contract;
        Assert.Equal("com.digitalbrain.start-ui", contract.BehaviorContractId);
        Assert.Equal(1, contract.ContractMajorVersion);
        var contractCase = Assert.Single(contract.Cases);
        Assert.Equal("case.scene", contractCase.CaseId);
        Assert.Equal(1, contractCase.CaseSchemaVersion);
        Assert.Equal("{\"properties\":{\"scene\":{\"type\":\"string\"}},\"type\":\"object\"}", contractCase.PayloadSchemaJson);
        Assert.Equal("{\"properties\":{\"opened\":{\"type\":\"boolean\"}},\"type\":\"object\"}", contract.ResultSchemaJson);
        Assert.Equal("public sealed class StartUi { }\n", envelope.ProgramSource);
        Assert.Equal(
            "Feature: start-ui\n  Scenario: alpha path\n    Then alpha succeeds\n  Scenario: zulu path\n    Then zulu succeeds\n",
            envelope.FeatureSource);
        Assert.Equal("{\"libraries\":{},\"version\":1}", envelope.PackageLockJson);
        Assert.Equal([0, 1, 2, 3], envelope.BehaviorAssembly.ToArray());
        Assert.Equal("{\"runtimeTarget\":{\"name\":\"net10.0\"}}", envelope.BehaviorDependenciesJson);
        Assert.Equal("{\"diagnostics\":[],\"languageVersion\":\"Preview\",\"policy\":\"contract-only-v1\",\"roslyn\":\"5.6.0\",\"sdk\":\"11.0.100-preview.6\",\"succeeded\":true}", envelope.CompilerEvidenceJson);
        Assert.Equal("{\"policy\":\"v1\",\"result\":\"accepted\"}", envelope.AdmissionEvidenceJson);
        Assert.Equal("{\"passed\":true,\"scenarios\":2}", envelope.BddEvidenceJson);
        Assert.Equal(artifact.Digest, BehaviorArtifactDigest.Compute(artifact.Bytes));
    }

    [Theory(DisplayName = "Reader rejects unsafe paths, alternate authored casing, and case-insensitive name collisions")]
    [InlineData("../escape.dll")]
    [InlineData("/absolute.dll")]
    [InlineData("C:/absolute.dll")]
    [InlineData("ARTIFACT/behavior.dll")]
    [InlineData("behavior.cs")]
    [InlineData("BEHAVIOR.cs")]
    [InlineData("program.cs")]
    [InlineData("features/alpha.feature")]
    public void ReaderRejectsUnsafeOrCaseCollidingEntryNames(string entry)
        => Assert.Throws<BehaviorArtifactException>(() => ReadWithExtraEntry(entry));

    [Fact(DisplayName = "Reader rejects symbolic-link entries before extracting any artifact data")]
    public void ReaderRejectsLinkEntries()
    {
        var bytes = CreateZip((archive, _) =>
        {
            var link = archive.CreateEntry("Behavior.link", CompressionLevel.NoCompression);
            link.ExternalAttributes = unchecked((int)0xA0000000);
            WriteText(link, "artifact/Behavior.dll");
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects exact duplicate artifact entry names")]
    public void ReaderRejectsDuplicateEntries()
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry("Behavior.cs", CompressionLevel.NoCompression), "duplicate"));

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader rejects names outside the closed envelope")]
    public void ReaderRejectsUnknownEntries()
    {
        var bytes = CreateZip((archive, _) => WriteText(archive.CreateEntry("surprise.txt", CompressionLevel.NoCompression), "no"));

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }

    [Fact(DisplayName = "Reader requires every envelope entry including both authored files")]
    public void ReaderRejectsMissingRequiredEntry()
    {
        var bytes = CreateRequiredZip(skip: "evidence/bdd.json");
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));

        var missingFeature = CreateRequiredZip(skip: "Behavior.feature");
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(missingFeature));

        var missingProgram = CreateRequiredZip(skip: "Behavior.cs");
        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(missingProgram));
    }

    [Fact(DisplayName = "Reader rejects more than 128 entries before reading their contents")]
    public void ReaderRejectsTooManyEntries()
    {
        var bytes = CreateZip((archive, _) =>
        {
            for (var index = 0; index < 130; index++)
            {
                WriteText(archive.CreateEntry($"extra{index:D3}.bin", CompressionLevel.NoCompression), "x");
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
            archive.GetEntry("artifact/Behavior.dll")!.Delete();
            WriteBytes(
                archive.CreateEntry("artifact/Behavior.dll", CompressionLevel.NoCompression),
                new byte[16 * 1024 * 1024]);

            for (var index = 0; index < 3; index++)
            {
                WriteBytes(
                    archive.CreateEntry($"pad{index}.bin", CompressionLevel.NoCompression),
                    new byte[16 * 1024 * 1024]);
            }

            WriteBytes(archive.CreateEntry("pad4.bin", CompressionLevel.NoCompression), [0]);
        });

        Assert.Throws<BehaviorArtifactException>(() => CanonicalArtifactReader.Read(bytes));
    }
}
