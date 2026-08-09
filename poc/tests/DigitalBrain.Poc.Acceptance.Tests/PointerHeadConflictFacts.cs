using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class PointerHeadConflictFacts
{
    [Fact]
    public async Task TransitionRefusesAHeadAdvancedAfterItsVerifiedPreflightSnapshot()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var concurrentStore = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var firstCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var secondCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                "head-conflict",
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
        var originalAttachment = initial.Attachment!;
        var expectedHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var transition = supervisor.BeginPromotionAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            HostFault.PauseAfterIngressClosedBeforeDrain,
            TestContext.Current.CancellationToken);
        await supervisor.WaitUntilIngressClosedAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        var concurrentlyAdvanced = pointers.Sign(ActiveCandidatePointer.Next(
            expectedHead,
            secondCandidate.Id));
        Assert.True((await concurrentStore.TryAdvancePointerHeadAsync(
            expectedHead,
            concurrentlyAdvanced,
            TestContext.Current.CancellationToken)).Succeeded);
        supervisor.ReleaseTestFault();

        var conflicted = await transition;
        Assert.False(conflicted.Succeeded);
        Assert.Equal(PromotionFailure.PointerHeadConflict, conflicted.Failure);
        var currentHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Equal(concurrentlyAdvanced.PayloadHash, currentHead.CurrentPayloadHash);
        await Assert.ThrowsAsync<HostQuiescingException>(() => originalAttachment.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("conflict-fences-old-run", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await originalAttachment.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TamperedPointerDuringPreflightFencesTheOldWholeRunInsteadOfReopeningIt()
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
                "tampered-during-preflight",
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
        var transition = supervisor.BeginPromotionAsync(
            owners.PrincipalForTest("owner-a"),
            secondCandidate.Id,
            HostFault.PauseAfterIngressClosedBeforeDrain,
            TestContext.Current.CancellationToken);
        await supervisor.WaitUntilIngressClosedAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        var active = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        await store.ReplacePointerFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            active with { Signature = "corrupt" },
            TestContext.Current.CancellationToken);
        supervisor.ReleaseTestFault();

        var rejected = await transition;
        Assert.False(rejected.Succeeded);
        Assert.Equal(PromotionFailure.PointerHeadConflict, rejected.Failure);
        await Assert.ThrowsAsync<HostQuiescingException>(() => initial.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("tamper-must-not-reopen-old-run", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await initial.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestartFencesTheOldWholeRunWhenItsCurrentPointerNoLongerVerifies()
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
        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);
        var active = await store.ReadPointerAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        await store.ReplacePointerFileForTestAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            active with { Signature = "corrupt" },
            TestContext.Current.CancellationToken);

        var restarted = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        Assert.False(restarted.Succeeded);
        Assert.Equal(BootFailure.InvalidPointerSignature, restarted.Failure);
        await Assert.ThrowsAsync<HostQuiescingException>(() => initial.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("invalid-pointer-fences-restart", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await initial.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestartFencesTheOldWholeRunWhenLedgerVerificationThrows()
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
        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);
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

        var restarted = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        Assert.False(restarted.Succeeded);
        Assert.Equal(BootFailure.CandidateVerificationFailed, restarted.Failure);
        await Assert.ThrowsAsync<HostQuiescingException>(() => initial.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("ledger-tamper-fences-restart", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await initial.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken));
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(path, recursive: true);
    }
}
