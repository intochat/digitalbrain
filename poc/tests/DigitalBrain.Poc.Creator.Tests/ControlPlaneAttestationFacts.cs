using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Creator.Tests;

public sealed class ControlPlaneAttestationFacts
{
    [Fact]
    public async Task QuarantineDiagnosticsAreImmutableAndOutsideCandidateDirectory()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var owners = new TestOwnerAuthority();
        var store = new TrustedCandidateCatalogStore(run, owners.CreateAttestationSigner());
        var id = new string('a', 64);

        await store.WriteDiagnosticAsync(
            new CandidateQuarantineDiagnostic(id, "scenario", "route did not settle"),
            TestContext.Current.CancellationToken);

        var diagnostic = await store.ReadDiagnosticAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal("scenario", diagnostic.Stage);
        Assert.Equal("route did not settle", diagnostic.Detail);
        Assert.False(diagnostic.Path.StartsWith(run.CandidateRoot, StringComparison.OrdinalIgnoreCase));
        await Assert.ThrowsAsync<IOException>(() => store.WriteDiagnosticAsync(
            new CandidateQuarantineDiagnostic(id, "scenario", "replacement"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void MalformedSignatureEncodingIsRejectedWithoutThrowing()
    {
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var signed = signer.Sign(CompletePayload());

        Assert.False(signer.Verify(signed with { Signature = "not-base64" }));
        Assert.False(signer.Verify(signed with { PublicKey = "not-base64" }));
    }

    [Fact]
    public void NullPayloadAndGrantListsAreRejectedWithoutThrowing()
    {
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var signed = signer.Sign(CompletePayload());

        Assert.False(signer.Verify((CandidateAttestation)null!));
        Assert.False(signer.Verify(signed with { Payload = null! }));
        Assert.False(signer.Verify(signed with
        {
            Payload = signed.Payload with { GrantedInputAliases = null! },
        }));
    }

    [Fact]
    public void SigningRejectsCandidateIdThatDoesNotEqualSourceHash()
    {
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();

        Assert.Throws<CryptographicException>(() => signer.Sign(
            CompletePayload() with { CandidateId = new string('e', 64) }));
    }

    [Fact]
    public async Task MutableCandidateMetadataCannotReplaceExternalAttestation()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var store = new TrustedCandidateCatalogStore(run, signer);
        var payload = CreatePayload(compiled, run);
        await store.WriteAttestationAsync(signer.Sign(payload), TestContext.Current.CancellationToken);
        await repository.ReplaceEvidenceMirrorAsync(
            compiled.Id,
            run,
            "{\"sourceHash\":\"attacker-chosen\"}",
            TestContext.Current.CancellationToken);

        var verification = await store.VerifyForBootAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);

        Assert.False(verification.Succeeded);
        Assert.Equal(AttestationFailure.CandidateMetadataHash, verification.Failure);
    }

    [Fact]
    public async Task BootVerificationClosesMalformedAttestationsAndMissingCandidateArtifacts()
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var store = new TrustedCandidateCatalogStore(run, signer);
        var attestationPath = Path.Combine(
            run.ControlPlaneRoot,
            "attestations",
            $"{compiled.Id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(attestationPath)!);

        foreach (var contents in new[]
        {
            "{",
            "[]",
            "{\"payload\":null}",
            "{\"payload\":{\"grantedInputAliases\":null}}",
        })
        {
            await File.WriteAllTextAsync(attestationPath, contents, TestContext.Current.CancellationToken);

            var malformed = await store.VerifyForBootAsync(
                compiled.Id,
                TestContext.Current.CancellationToken);

            Assert.False(malformed.Succeeded);
            Assert.Equal(AttestationFailure.MalformedAttestation, malformed.Failure);
        }

        File.Delete(attestationPath);
        await store.WriteAttestationAsync(
            signer.Sign(CreatePayload(compiled, run)),
            TestContext.Current.CancellationToken);
        File.SetAttributes(
            attestationPath,
            File.GetAttributes(attestationPath) & ~FileAttributes.ReadOnly);
        var validAttestation = await File.ReadAllTextAsync(
            attestationPath,
            TestContext.Current.CancellationToken);
        var nullRunId = validAttestation.Replace(
            $"\"runId\": \"{run.RunId}\"",
            "\"runId\": null",
            StringComparison.Ordinal);
        Assert.NotEqual(validAttestation, nullRunId);
        await File.WriteAllTextAsync(
            attestationPath,
            nullRunId,
            TestContext.Current.CancellationToken);

        var missingRunId = await store.VerifyForBootAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);

        Assert.False(missingRunId.Succeeded);
        Assert.Equal(AttestationFailure.MalformedAttestation, missingRunId.Failure);
        File.Delete(attestationPath);
        await store.WriteAttestationAsync(
            signer.Sign(CreatePayload(compiled, run)),
            TestContext.Current.CancellationToken);
        File.SetAttributes(
            compiled.AssemblyPath,
            File.GetAttributes(compiled.AssemblyPath) & ~FileAttributes.ReadOnly);
        File.Delete(compiled.AssemblyPath);

        var missingAssembly = await store.VerifyForBootAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);

        Assert.False(missingAssembly.Succeeded);
        Assert.Equal(AttestationFailure.AssemblyUnavailable, missingAssembly.Failure);
        File.SetAttributes(
            compiled.SourcePath,
            File.GetAttributes(compiled.SourcePath) & ~FileAttributes.ReadOnly);
        File.Delete(compiled.SourcePath);

        var missingArtifact = await store.VerifyForBootAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);

        Assert.False(missingArtifact.Succeeded);
        Assert.Equal(AttestationFailure.SourceUnavailable, missingArtifact.Failure);
    }

    [Theory]
    [InlineData("attacker.cs")]
    [InlineData("attacker.csproj")]
    public async Task BootVerificationRejectsFilesOutsideTheCanonicalCandidateInventory(
        string fileName)
    {
        await using var run = PocDataRoot.Create(FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var store = new TrustedCandidateCatalogStore(run, signer);
        await store.WriteAttestationAsync(
            signer.Sign(CreatePayload(compiled, run)),
            TestContext.Current.CancellationToken);
        var injectedDirectory = Path.Combine(compiled.Directory, "nested");
        Directory.CreateDirectory(injectedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(injectedDirectory, fileName),
            fileName.EndsWith(".csproj", StringComparison.Ordinal) ? "<Project />" : "class Attacker;",
            TestContext.Current.CancellationToken);

        var verification = await store.VerifyForBootAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);

        Assert.False(verification.Succeeded);
        Assert.Equal(AttestationFailure.CandidateInventory, verification.Failure);
    }

    private static CandidateAttestationPayload CreatePayload(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot run) => new(
            compiled.Id,
            run.RunId,
            "owner-a",
            compiled.Manifest.FamilyId,
            compiled.Manifest.SourceHash,
            compiled.Manifest.AssemblyHash,
            compiled.Manifest.CandidateMetadataHash,
            Convert.ToHexString(SHA256.HashData("scenario"u8)).ToLowerInvariant())
        {
            Revision = $"quarantine-{compiled.Manifest.AssemblyHash}",
            Status = "awaitingOwnerApproval",
            SourcePath = "elon-chart.cs",
            AssemblyPath = "module.dll",
            GrantedInputAliases = [compiled.Intent.AttestedTriggerAlias],
            GrantedCandidateOutputAliases =
            [
                $"db.poc.family.{compiled.Intent.Family.Value}.matched.v{compiled.Intent.LocalSynapseSchemaVersion}",
            ],
            GrantedTrustedOutputAliases = ["db.poc.chart.add-point.v1"],
            GrantedTargetScopes = [compiled.Intent.ChartId],
            ResolvedReferences = compiled.Manifest.ResolvedReferences,
            NormalizedAstHash = compiled.Manifest.NormalizedAstHash,
            FixedHeaderHash = compiled.Manifest.FixedHeaderHash,
            CompilerHash = compiled.Manifest.CompilerHash,
            SdkHash = compiled.Manifest.SdkHash,
            ReferencesHash = compiled.Manifest.ReferencesHash,
            CapabilitiesHash = compiled.Manifest.CapabilitiesHash,
            ContractsHash = compiled.Manifest.ContractsHash,
            StateSchemaHash = compiled.Manifest.StateSchemaHash,
        };

    private static string FindPocRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solution = Path.Combine(current.FullName, "poc", "DigitalBrain.Poc.slnx");
            if (File.Exists(solution))
            {
                return Path.GetDirectoryName(solution)!;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the POC root.");
    }

    private static CandidateAttestationPayload CompletePayload() => new(
        new string('a', 64),
        "run-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "owner-a",
        "cf_aaaaaaaaaaaaaaaaaaaaaaaaaa",
        new string('a', 64),
        new string('b', 64),
        new string('c', 64),
        new string('d', 64))
    {
        Revision = $"quarantine-{new string('b', 64)}",
        Status = "awaitingOwnerApproval",
        SourcePath = "elon-chart.cs",
        AssemblyPath = "module.dll",
        GrantedInputAliases = ["db.poc.social.post-observed.v1"],
        GrantedCandidateOutputAliases = ["db.poc.family.cf_aaaaaaaaaaaaaaaaaaaaaaaaaa.matched.v1"],
        GrantedTrustedOutputAliases = ["db.poc.chart.add-point.v1"],
        GrantedTargetScopes = ["elon-chart"],
        ResolvedReferences = ["assembly|DigitalBrain.Poc.Abstractions"],
        NormalizedAstHash = new string('1', 64),
        FixedHeaderHash = new string('2', 64),
        CompilerHash = new string('3', 64),
        SdkHash = new string('4', 64),
        ReferencesHash = new string('5', 64),
        CapabilitiesHash = new string('6', 64),
        ContractsHash = new string('7', 64),
        StateSchemaHash = new string('8', 64),
    };
}
