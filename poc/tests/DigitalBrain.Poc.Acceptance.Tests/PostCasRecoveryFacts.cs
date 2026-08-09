using System.Diagnostics;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class PostCasRecoveryFacts
{
    [Fact]
    public async Task ActivationFaultAfterCasCompensatesBeforeOldIngressReopens()
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
                "post-cas-fault",
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

        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);
        var priorHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        var failed = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            HostFault.AfterPointerAdvanceBeforeActivation,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Equal(PromotionFailure.ActivationFailed, failed.Failure);
        var compensatedHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var verified = await store.VerifyActivePointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(verified.Succeeded);
        Assert.Equal(firstCandidate.Id, compensatedHead.CurrentCandidateSourceHash);
        Assert.Equal(firstCandidate.Id, verified.Pointer!.CandidateSourceHash);
        Assert.Equal(priorHead.Version + 2, compensatedHead.Version);

        var current = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Same(initial.Attachment, current);
        Assert.Equal(initial.ProcessId, current.ProcessId);
        await current.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("old-ingress-remains-authoritative", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LostAuthorityReleaseAcknowledgementReacquiresBeforeOldIngressReopens()
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
                "lost-authority-acknowledgement",
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

        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            HostFault.AfterAuthorityReleaseBeforeAcknowledgement,
            TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);

        var failed = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Equal(PromotionFailure.ActivationFailed, failed.Failure);
        var current = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Same(initial.Attachment, current);
        Assert.Equal(initial.ProcessId, current.ProcessId);
        await Assert.ThrowsAsync<HostQuiescingException>(async () => await HostAuthorityLease
            .AcquireForActiveHostAsync(
                root,
                delegatedBySignedSupervisor: true,
                authorityControlToken: "forged-after-release",
                TestContext.Current.CancellationToken));
        await current.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("authority-was-reacquired-before-reopen", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnrecoverablePostCasFailureFencesTheOldWholeRunAndClearsRoutes()
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
                "unrecoverable-post-cas",
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
        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);

        var failed = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            HostFault.ForceActivationRecoveryFailure,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Equal(PromotionFailure.ActivationRecoveryFailed, failed.Failure);
        await Assert.ThrowsAsync<HostQuiescingException>(() => initial.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("unrecoverable-must-fence-old-run", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await initial.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OldProcessExitAtReplacementRetirementStillInstallsTheReadyWholeRun()
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
                "old-process-exit",
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
        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            firstCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);

        var transition = supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            HostFault.PauseBeforeOldRunRetirement,
            TestContext.Current.CancellationToken);
        await supervisor.WaitUntilOldRunReadyToRetireAsync(TestContext.Current.CancellationToken);
        using (var oldProcess = Process.GetProcessById(initial.ProcessId))
        {
            oldProcess.Kill(entireProcessTree: true);
        }

        await initial.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        supervisor.ReleaseTestFault();
        var replacement = await transition;

        Assert.True(replacement.Succeeded);
        Assert.NotEqual(initial.ProcessId, replacement.ProcessId);
        var current = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Equal(replacement.ProcessId, current.ProcessId);
        await current.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("replacement-remains-authoritative", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FirstPostCasActivationFailureLeavesAnAuthenticatedEmptyLineageThatCanRetryAndColdRestart()
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

        var failed = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            HostFault.AfterPointerAdvanceBeforeActivation,
            TestContext.Current.CancellationToken);

        Assert.False(failed.Succeeded);
        Assert.Equal(PromotionFailure.ActivationFailed, failed.Failure);
        var emptyHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Equal(2, emptyHead.Version);
        Assert.Equal(new string('0', 64), emptyHead.CurrentCandidateSourceHash);
        Assert.Empty(await store.ReadAllVerifiedActiveCandidatesAsync(TestContext.Current.CancellationToken));

        var retried = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(retried.Succeeded);
        var restarted = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(restarted.Succeeded);
        Assert.NotEqual(retried.ProcessId, restarted.ProcessId);
    }
}
