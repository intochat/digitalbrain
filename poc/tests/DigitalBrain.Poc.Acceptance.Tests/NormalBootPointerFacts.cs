using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class NormalBootPointerFacts
{
    [Fact]
    public async Task ShippingNormalBootDoesNotExposeTheTestRaceBarrierThroughEnvironment()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();
        var token = Guid.NewGuid().ToString("N");
        var prefix = Path.Combine(root.RootPath, "test-normal-boot-pause-" + token);
        await File.WriteAllTextAsync(
            prefix + ".release",
            string.Empty,
            TestContext.Current.CancellationToken);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            startInfo => startInfo.Environment["DIGITALBRAIN_POC_TEST_NORMAL_BOOT_PAUSE_TOKEN"] = token,
            TestContext.Current.CancellationToken);

        Assert.True(boot.Succeeded);
        Assert.False(File.Exists(prefix + ".observed"));
    }

    [Fact]
    public async Task PointerSelectedNormalBootCannotOverlapSupervisorOwnedAuthority()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        var promoted = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(promoted.Succeeded);

        await using var direct = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(direct.Succeeded);
        await promoted.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("supervisor-remains-authoritative", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ForgedSupervisorDelegationCannotBypassStandaloneHostAuthority()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();
        var head = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        using var forgedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forgedSigner = new PointerSigner(
            payload => forgedKey.SignData(
                payload,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
            forgedKey.ExportSubjectPublicKeyInfo());
        var forgedDelegation = forgedSigner.SignHostAuthorityDelegation(
            new HostAuthorityDelegationPayload(
                root.RunId,
                head.CurrentPayloadHash,
                PointerSigner.ActiveSelectionHash([candidate.Id])));

        await using var forged = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            startInfo =>
            {
                startInfo.Environment[ActiveHostBootstrap.AuthorityDelegationEnvironment] =
                    JsonSerializer.Serialize(forgedDelegation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                startInfo.Environment[ActiveHostBootstrap.PreflightExpectedHeadEnvironment] =
                    JsonSerializer.Serialize(head, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                startInfo.Environment[HostAuthorityLease.ControlTokenEnvironment] = "forged-control-token";
            },
            TestContext.Current.CancellationToken);

        Assert.False(forged.Succeeded);
        await using var normal = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);
        Assert.True(normal.Succeeded);
    }

    [Fact]
    public async Task DirectNormalHostRejectsRawAuthorityReleaseWithoutTheSupervisorCapability()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        await using var normal = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);
        Assert.True(normal.Succeeded);
        var response = await normal.SendRawAsync(
            JsonSerializer.Serialize(new
            {
                id = "forged-release",
                command = "release-host-authority",
                payload = new { token = "forged-control-token" },
            }),
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(response);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            nameof(AuthorizationException),
            document.RootElement.GetProperty("errorType").GetString());
    }

    [Fact]
    public async Task NormalBootRefusesASelectionThatChangesBetweenObservationAndAuthorityAcquisition()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var firstCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var secondCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                "normal-boot-selection-race",
                "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            firstCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await PromotionFacts.AttestAndApproveAsync(
            secondCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();
        var expected = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var token = Guid.NewGuid().ToString("N");
        var prefix = Path.Combine(root.RootPath, "test-normal-boot-pause-" + token);
        var bootTask = PointerSelectedHostProcess.StartRaceFixtureAsync(
            root,
            owners,
            token,
            TestContext.Current.CancellationToken);

        await WaitForFileAsync(prefix + ".observed", TestContext.Current.CancellationToken);
        var next = pointers.Sign(ActiveCandidatePointer.Next(expected, secondCandidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            expected,
            next,
            TestContext.Current.CancellationToken)).Succeeded);
        await File.WriteAllTextAsync(prefix + ".release", string.Empty, TestContext.Current.CancellationToken);
        await using var staleBoot = await bootTask;

        Assert.False(staleBoot.Succeeded);
        Assert.Equal(
            secondCandidate.Id,
            (await store.ReadPointerAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken)).CandidateSourceHash);
        await using var currentBoot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);
        Assert.True(currentBoot.Succeeded);
        Assert.Equal([secondCandidate.Id], currentBoot.ActiveSourceHashes);
    }

    [Fact]
    public async Task NormalBootRefusesAChangedPointerHeadEvenWhenTheCandidateSourceIsUnchanged()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();
        var expected = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var token = Guid.NewGuid().ToString("N");
        var prefix = Path.Combine(root.RootPath, "test-normal-boot-pause-" + token);
        var bootTask = PointerSelectedHostProcess.StartRaceFixtureAsync(
            root,
            owners,
            token,
            TestContext.Current.CancellationToken);

        await WaitForFileAsync(prefix + ".observed", TestContext.Current.CancellationToken);
        var noOpLineageAdvance = pointers.Sign(ActiveCandidatePointer.Next(expected, candidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            expected,
            noOpLineageAdvance,
            TestContext.Current.CancellationToken)).Succeeded);
        await File.WriteAllTextAsync(prefix + ".release", string.Empty, TestContext.Current.CancellationToken);
        await using var staleBoot = await bootTask;

        Assert.False(staleBoot.Succeeded);
        Assert.Equal(
            noOpLineageAdvance.PayloadHash,
            (await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken)).CurrentPayloadHash);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task PointerSelectedNormalBootFailsWhenAnyEstablishedActivePointerIsDeleted(
        bool deleteHeadToo,
        bool deleteDirectory,
        bool replaceHeadWithCanonicalEmpty)
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var first = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var secondFamily = CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb");
        var second = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(secondFamily, "second-owner", "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            first,
            root,
            store,
            attestations,
            catalog,
            owners);
        await PromotionFacts.AttestAndApproveAsync(
            second,
            root,
            store,
            attestations,
            catalog,
            owners,
            "owner-b");
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            first.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-b"),
            second.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        var pointerPath = PointerCurrentPath(root, "owner-a", ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        var headPath = PointerHeadPath(root, "owner-a", ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        if (deleteDirectory)
        {
            DeleteDirectory(Path.GetDirectoryName(pointerPath)!);
        }
        else
        {
            File.Delete(pointerPath);
            if (deleteHeadToo)
            {
                File.Delete(headPath);
            }

            if (replaceHeadWithCanonicalEmpty)
            {
                await File.WriteAllTextAsync(
                    headPath,
                    JsonSerializer.Serialize(
                        CandidatePointerHead.Empty(
                            "owner-a",
                            ElonChartAuthoringIntent.DefaultTrustedFixture.Family.Value),
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    TestContext.Current.CancellationToken);
            }
        }

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
    }

    [Fact]
    public async Task PointerSelectedNormalBootFailsClosedWhenActiveCandidateEvidenceIsDeleted()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var headPath = PointerHeadPath(root, "owner-a", ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        var headBefore = await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken);
        var evidence = Path.Combine(candidate.Directory, "candidate.json");
        File.SetAttributes(evidence, File.GetAttributes(evidence) & ~FileAttributes.ReadOnly);
        File.Delete(evidence);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        Assert.Equal(
            headBefore,
            await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.Source)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.Assembly)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.CandidateMetadata)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.FixedHeader)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.FixedReference)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.CapabilityGrant)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.QuarantineEvidence)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.SignedAttestation)]
    [InlineData(PostApprovalTamperFacts.PostApprovalTamper.SignedApproval)]
    public async Task PointerSelectedNormalBootRejectsEveryPostApprovalTamper(
        PostApprovalTamperFacts.PostApprovalTamper tamper)
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var pointerBefore = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var headPath = PointerHeadPath(root, "owner-a", ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        var headBefore = await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken);

        await supervisor.DisposeAsync();
        await PostApprovalTamperFacts.TamperAsync(
            tamper,
            root,
            candidate,
            TestContext.Current.CancellationToken);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        Assert.Equal(
            pointerBefore,
            await store.ReadPointerAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            headBefore,
            await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PointerTamper.ReplayedSignedPointer)]
    [InlineData(PointerTamper.ReplayedSignedPointerAndHead)]
    [InlineData(PointerTamper.InvalidSignature)]
    public async Task PointerSelectedNormalBootRejectsReplayAndInvalidSignature(
        PointerTamper tamper)
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var firstCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var secondCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                "normal-boot-pointer",
                "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            firstCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await PromotionFacts.AttestAndApproveAsync(
            secondCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var firstPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var secondPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var headBefore = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var headPath = PointerHeadPath(root, "owner-a", ElonChartAuthoringIntent.DefaultTrustedFixture.Family);

        if (tamper == PointerTamper.ReplayedSignedPointerAndHead)
        {
            await store.ReplacePointerFileForTestAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                firstPointer,
                TestContext.Current.CancellationToken);
            await store.ReplacePointerHeadFileForTestAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                CandidatePointerHead.From(firstPointer),
                TestContext.Current.CancellationToken);
        }
        else
        {
            await store.ReplacePointerFileForTestAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                tamper == PointerTamper.ReplayedSignedPointer
                    ? firstPointer
                    : secondPointer with { Signature = "corrupt" },
                TestContext.Current.CancellationToken);
        }

        var headBytesAfterTamper = await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken);
        await supervisor.DisposeAsync();
        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        if (tamper == PointerTamper.ReplayedSignedPointerAndHead)
        {
            await Assert.ThrowsAsync<CryptographicException>(() => store.VerifyActivePointerAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.False((await store.VerifyActivePointerAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken)).Succeeded);
        }

        Assert.Equal(
            headBytesAfterTamper,
            await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken));
        if (tamper != PointerTamper.ReplayedSignedPointerAndHead)
        {
            var persistedHead = JsonSerializer.Deserialize<CandidatePointerHead>(
                await File.ReadAllBytesAsync(headPath, TestContext.Current.CancellationToken),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Assert.Equal(headBefore, persistedHead);
        }
    }

    [Fact]
    public async Task PointerSelectedNormalBootFailsClosedWhenTheGlobalLedgerIsDeletedAndAnOldPointerIsReplayed()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var firstPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var firstHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var secondPointer = pointers.Sign(ActiveCandidatePointer.Next(firstHead, candidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            firstHead,
            secondPointer,
            TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        DeleteDirectory(Path.Combine(root.RootPath, "pointer-ledger"));
        await store.ReplacePointerFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            firstPointer,
            TestContext.Current.CancellationToken);
        await store.ReplacePointerHeadFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            CandidatePointerHead.From(firstPointer),
            TestContext.Current.CancellationToken);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        await Assert.ThrowsAsync<CryptographicException>(() => store.VerifyActivePointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PointerSelectedNormalBootFailsClosedWhenOnlyTheNewerLedgerRecordIsDeletedAndV1IsReplayed()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var firstPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var firstHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var secondPointer = pointers.Sign(ActiveCandidatePointer.Next(firstHead, candidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            firstHead,
            secondPointer,
            TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        DeleteFile(Path.Combine(root.RootPath, "pointer-ledger", secondPointer.PayloadHash + ".json"));
        await store.ReplacePointerFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            firstPointer,
            TestContext.Current.CancellationToken);
        await store.ReplacePointerHeadFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            CandidatePointerHead.From(firstPointer),
            TestContext.Current.CancellationToken);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        await Assert.ThrowsAsync<CryptographicException>(() => store.VerifyActivePointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PointerLedgerAnchorTamper.Missing)]
    [InlineData(PointerLedgerAnchorTamper.Tampered)]
    [InlineData(PointerLedgerAnchorTamper.RolledBack)]
    public async Task PointerSelectedNormalBootFailsClosedWhenTheTrustedLedgerAnchorIsNotCurrent(
        PointerLedgerAnchorTamper tamper)
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var firstPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var firstHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var secondPointer = pointers.Sign(ActiveCandidatePointer.Next(firstHead, candidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            firstHead,
            secondPointer,
            TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        var anchorCurrent = PointerLedgerAnchorCurrentPath(
            root,
            "owner-a",
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        Assert.True(File.Exists(anchorCurrent));
        switch (tamper)
        {
            case PointerLedgerAnchorTamper.Missing:
                DeleteFile(anchorCurrent);
                break;
            case PointerLedgerAnchorTamper.Tampered:
                File.SetAttributes(anchorCurrent, File.GetAttributes(anchorCurrent) & ~FileAttributes.ReadOnly);
                await File.WriteAllTextAsync(anchorCurrent, "tampered anchor", TestContext.Current.CancellationToken);
                break;
            case PointerLedgerAnchorTamper.RolledBack:
                await File.WriteAllBytesAsync(
                    anchorCurrent,
                    await File.ReadAllBytesAsync(
                        PointerLedgerAnchorHistoryPath(
                            root,
                            "owner-a",
                            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                            firstPointer.PayloadHash),
                        TestContext.Current.CancellationToken),
                    TestContext.Current.CancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tamper), tamper, null);
        }

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
    }

    [Fact]
    public async Task PointerSelectedNormalBootFailsClosedWhenEveryMutableLedgerReplicaIsRolledBack()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        Assert.True((await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var firstPointer = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var firstHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var secondPointer = pointers.Sign(ActiveCandidatePointer.Next(firstHead, candidate.Id));
        Assert.True((await store.TryAdvancePointerHeadAsync(
            firstHead,
            secondPointer,
            TestContext.Current.CancellationToken)).Succeeded);
        await supervisor.DisposeAsync();

        DeleteFile(Path.Combine(root.RootPath, "pointer-ledger", secondPointer.PayloadHash + ".json"));
        DeleteFile(PointerHistoryPath(
            root,
            "owner-a",
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            secondPointer.PayloadHash));
        var mutableAnchorCurrent = MutablePointerLedgerAnchorCurrentPath(
            root,
            "owner-a",
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family);
        if (File.Exists(mutableAnchorCurrent))
        {
            DeleteFile(MutablePointerLedgerAnchorHistoryPath(
                root,
                "owner-a",
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                secondPointer.PayloadHash));
            await File.WriteAllBytesAsync(
                mutableAnchorCurrent,
                await File.ReadAllBytesAsync(
                    MutablePointerLedgerAnchorHistoryPath(
                        root,
                        "owner-a",
                        ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                        firstPointer.PayloadHash),
                    TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
        }

        await store.ReplacePointerFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            firstPointer,
            TestContext.Current.CancellationToken);
        await store.ReplacePointerHeadFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            CandidatePointerHead.From(firstPointer),
            TestContext.Current.CancellationToken);

        await using var boot = await PointerSelectedHostProcess.StartAsync(
            root,
            owners,
            TestContext.Current.CancellationToken);

        Assert.False(boot.Succeeded);
        await Assert.ThrowsAsync<CryptographicException>(() => store.VerifyActivePointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    private static string PointerHeadPath(PocDataRoot root, string ownerId, CandidateFamilyId family) =>
        Path.Combine(
            root.ControlPlaneRoot,
            "pointers",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "head.json");

    private static string PointerCurrentPath(PocDataRoot root, string ownerId, CandidateFamilyId family) =>
        Path.Combine(
            root.ControlPlaneRoot,
            "pointers",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "current.json");

    private static string PointerHistoryPath(
        PocDataRoot root,
        string ownerId,
        CandidateFamilyId family,
        string payloadHash) =>
        Path.Combine(
            root.ControlPlaneRoot,
            "pointers",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "history",
            payloadHash + ".json");

    private static string PointerLedgerAnchorCurrentPath(
        PocDataRoot root,
        string ownerId,
        CandidateFamilyId family) =>
        Path.Combine(
            root.PointerLedgerAuthorityPath,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "current.json");

    private static string PointerLedgerAnchorHistoryPath(
        PocDataRoot root,
        string ownerId,
        CandidateFamilyId family,
        string payloadHash) =>
        Path.Combine(
            root.PointerLedgerAuthorityPath,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "history",
            payloadHash + ".json");

    private static string MutablePointerLedgerAnchorCurrentPath(
        PocDataRoot root,
        string ownerId,
        CandidateFamilyId family) =>
        Path.Combine(
            root.RootPath,
            "pointer-ledger-anchor",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "current.json");

    private static string MutablePointerLedgerAnchorHistoryPath(
        PocDataRoot root,
        string ownerId,
        CandidateFamilyId family,
        string payloadHash) =>
        Path.Combine(
            root.RootPath,
            "pointer-ledger-anchor",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant(),
            family.Value,
            "history",
            payloadHash + ".json");

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(path, recursive: true);
    }

    private static void DeleteFile(string path)
    {
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }

    private static async Task WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    public enum PointerTamper
    {
        ReplayedSignedPointer,
        ReplayedSignedPointerAndHead,
        InvalidSignature,
    }

    public enum PointerLedgerAnchorTamper
    {
        Missing,
        Tampered,
        RolledBack,
    }
}
