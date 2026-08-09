using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class QuarantineFacts
{
    [Fact]
    public async Task NormalAuthoringReservationRejectsDifferentOwnerBeforeAttestation()
    {
        await using var run = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var ownerA = owners.PrincipalForTest("owner-a");
        var ownerB = owners.PrincipalForTest("owner-b");
        var authored = await new CandidateAuthoringService(repository, run).CompileAsync(
            ownerA,
            "elon-chart",
            "elonmusk",
            TestContext.Current.CancellationToken);
        var signer = owners.CreateAttestationSigner();
        var controlPlane = new TrustedCandidateCatalogStore(run, signer);
        var quarantine = new QuarantineRunner(repository, controlPlane, signer, owners.ExportSessions());

        await Assert.ThrowsAsync<AuthorizationException>(() => quarantine.RunAsync(
            authored,
            run,
            ownerB,
            TestContext.Current.CancellationToken));

        Assert.True(await new FileCandidateFamilyRegistry(run).IsReservedForAsync(
            ownerA,
            authored.Family,
            TestContext.Current.CancellationToken));
        File.Delete(Path.Combine(run.ControlPlaneRoot, "candidate-families.json"));

        await Assert.ThrowsAsync<AuthorizationException>(() => quarantine.RunAsync(
            authored,
            run,
            ownerA,
            TestContext.Current.CancellationToken));

        Assert.False(await controlPlane.ExistsAsync(authored.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EvidenceDriftRecordsDiagnosticWithoutAttestationOrActivation()
    {
        await using var run = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var controlPlane = new TrustedCandidateCatalogStore(run, signer);
        var quarantine = new QuarantineRunner(repository, controlPlane, signer, owners.ExportSessions());
        var drifted = compiled with
        {
            Manifest = compiled.Manifest with { ReferencesHash = new string('f', 64) },
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => quarantine.RunTrustedFixtureAsync(
            drifted,
            run,
            TestContext.Current.CancellationToken));

        Assert.False(await controlPlane.ExistsAsync(compiled.Id, TestContext.Current.CancellationToken));
        var diagnostic = await controlPlane.ReadDiagnosticAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal("quarantine", diagnostic.Stage);
        Assert.Null(await controlPlane.ActiveAsync(
            "owner-a",
            compiled.Intent.Family.Value,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            CandidateStatus.AwaitingQuarantine,
            (await repository.ReadAsync(
                compiled.Id,
                run,
                TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData("attacker.cs")]
    [InlineData("attacker.csproj")]
    public async Task ExtraCandidateSourceOrProjectBeforeQuarantineRecordsDiagnosticWithoutAttestation(
        string fileName)
    {
        await using var run = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var injectedDirectory = Path.Combine(compiled.Directory, "nested");
        Directory.CreateDirectory(injectedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(injectedDirectory, fileName),
            fileName.EndsWith(".csproj", StringComparison.Ordinal) ? "<Project />" : "class Attacker;",
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var controlPlane = new TrustedCandidateCatalogStore(run, signer);
        var quarantine = new QuarantineRunner(repository, controlPlane, signer, owners.ExportSessions());

        await Assert.ThrowsAsync<InvalidDataException>(() => quarantine.RunTrustedFixtureAsync(
            compiled,
            run,
            TestContext.Current.CancellationToken));

        Assert.False(await controlPlane.ExistsAsync(compiled.Id, TestContext.Current.CancellationToken));
        Assert.Equal(
            "quarantine",
            (await controlPlane.ReadDiagnosticAsync(
                compiled.Id,
                TestContext.Current.CancellationToken)).Stage);
    }

    [Fact]
    public async Task CancellationAfterAttestationStagingLeavesNoPublishedAttestationAndImmutableDiagnostic()
    {
        await using var run = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        using var cancellation = new CancellationTokenSource();
        var controlPlane = new TrustedCandidateCatalogStore(
            run,
            signer,
            _ =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            });
        var quarantine = new QuarantineRunner(repository, controlPlane, signer, owners.ExportSessions());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => quarantine.RunTrustedFixtureAsync(
            compiled,
            run,
            cancellation.Token));

        Assert.False(await controlPlane.ExistsAsync(compiled.Id, TestContext.Current.CancellationToken));
        Assert.Equal(
            AttestationFailure.Missing,
            (await controlPlane.VerifyForBootAsync(
                compiled.Id,
                TestContext.Current.CancellationToken)).Failure);
        var diagnostic = await controlPlane.ReadDiagnosticAsync(
            compiled.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal("quarantine", diagnostic.Stage);
        await Assert.ThrowsAsync<IOException>(() => controlPlane.WriteDiagnosticAsync(
            new CandidateQuarantineDiagnostic(compiled.Id, "quarantine", "replacement"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QuarantineRoutesAcrossBothGeneratedNeuronsAndAttestsExternally()
    {
        await using var run = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var controlPlane = new TrustedCandidateCatalogStore(run, signer);
        var quarantine = new QuarantineRunner(repository, controlPlane, signer, owners.ExportSessions());

        var result = await quarantine.RunTrustedFixtureAsync(
            compiled,
            run,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
            result.JournalKindsForInput("post-1"));
        Assert.Equal(
            ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "AddChartPoint", "ChartPointAdded"],
            result.RawJournalKinds);
        Assert.Equal(1, result.ChartPointCount);
        Assert.True(result.AttestationSignatureVerified);
        Assert.Equal(CandidateStatus.AwaitingOwnerApproval, result.Manifest.Status);
        Assert.Equal(compiled.Manifest.CandidateMetadataHash, result.Manifest.CandidateMetadataHash);
        var immutableMirror = await repository.ReadAsync(
            result.Id,
            run,
            TestContext.Current.CancellationToken);
        Assert.Equal(CandidateStatus.AwaitingQuarantine, immutableMirror.Status);
        Assert.Null(await controlPlane.ActiveAsync(
            result.AuthenticatedOwner,
            result.CandidateFamily,
            TestContext.Current.CancellationToken));
        var attestation = await controlPlane.ReadAttestationAsync(
            result.Id,
            TestContext.Current.CancellationToken);
        Assert.True(signer.Verify(attestation));
        Assert.Equal(result.Manifest.CandidateMetadataHash, attestation.Payload.CandidateMetadataHash);
        var trusted = await controlPlane.ReadTrustedCandidateAsync(
            result.Id,
            TestContext.Current.CancellationToken);
        Assert.Equal("owner-a", trusted.OwnerId);
        Assert.Equal(result.CandidateFamily, trusted.FamilyId);
        Assert.Equal(result.Id, trusted.CandidateId);
        Assert.Equal("awaitingOwnerApproval", trusted.Status);
        Assert.Equal("elon-chart.cs", trusted.SourcePath);
        Assert.Equal("module.dll", trusted.AssemblyPath);
        Assert.Equal(["db.poc.social.post-observed.v1"], trusted.GrantedInputAliases);
        Assert.Equal(["elon-chart"], trusted.GrantedTargetScopes);
        Assert.NotEmpty(trusted.ResolvedReferences);
    }

    [Fact]
    public async Task DisposingQuarantineRunDeletesCandidateAndControlPlaneEvidence()
    {
        var pocRoot = HostProcess.FindPocRoot();
        var run = PocDataRoot.Create(pocRoot);
        var repository = new CandidateRepository();
        var compiled = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            run,
            TestContext.Current.CancellationToken);
        var owners = new TestOwnerAuthority();
        var signer = owners.CreateAttestationSigner();
        var controlPlane = new TrustedCandidateCatalogStore(run, signer);
        var candidate = await new QuarantineRunner(
            repository,
            controlPlane,
            signer,
            owners.ExportSessions()).RunTrustedFixtureAsync(
                compiled,
                run,
                TestContext.Current.CancellationToken);

        await run.DisposeAsync();

        Assert.False(Directory.Exists(candidate.Directory));
        Assert.False(await controlPlane.ExistsAsync(candidate.Id, TestContext.Current.CancellationToken));
        Assert.Empty(await PocDataRoot.FindArtifactsForRunAsync(
            pocRoot,
            run.RunId,
            TestContext.Current.CancellationToken));
    }
}
