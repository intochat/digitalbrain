using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class WholeRunSupervisorFacts
{
    [Fact]
    public async Task ChildHeldAuthoritySurvivesControllerLeaseLossAndFencesAFreshSupervisor()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var controller = await HostAuthorityLease.TryAcquireAsync(
            root,
            TestContext.Current.CancellationToken) ?? throw new InvalidOperationException(
                "The initial controller must acquire the transition lease.");
        await using var child = await HostAuthorityLease.AcquireForActiveHostAsync(
            root,
            delegatedBySignedSupervisor: true,
            authorityControlToken: controller.ControlToken,
            cancellationToken: TestContext.Current.CancellationToken);

        await controller.DisposeAsync();
        var fresh = await HostAuthorityLease.TryAcquireAsync(root, TestContext.Current.CancellationToken);

        Assert.Null(fresh);
    }

    [Fact]
    public async Task PromotingASecondOwnerRetiresTheOldWholeRunAndRoutesBothOwnersToOneProcess()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var candidateA = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var familyB = CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb");
        var candidateB = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(familyB, "owner-b-chart", "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            candidateA,
            root,
            store,
            attestations,
            catalog,
            owners);
        await PromotionFacts.AttestAndApproveAsync(
            candidateB,
            root,
            store,
            attestations,
            catalog,
            owners,
            "owner-b");
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);

        var first = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            candidateA.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded);
        var oldAttachment = first.Attachment!;
        await oldAttachment.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("before-handoff", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        var durableBefore = await oldAttachment.JournalKindsAsync(
            owners.SessionFor("owner-a"),
            TestContext.Current.CancellationToken);
        Assert.NotEmpty(durableBefore);

        await using (var freshSupervisor = new HostSupervisor(root, store, pointers, owners))
        {
            var refused = await freshSupervisor.PromoteAsync(
                owners.PrincipalForTest("owner-b"),
                candidateB.Id,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(refused.Succeeded);
            Assert.Equal(PromotionFailure.HostAuthorityUnavailable, refused.Failure);
        }
        await oldAttachment.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("fresh-supervisor-cannot-overlap", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);

        var second = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-b"),
            candidateB.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(second.Succeeded);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        await Assert.ThrowsAsync<HostQuiescingException>(() => oldAttachment.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("rejected-old-attachment", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await oldAttachment.WaitForExitAsync(TestContext.Current.CancellationToken);

        var activeA = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var activeB = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-b"),
            familyB,
            TestContext.Current.CancellationToken);
        Assert.Equal(second.ProcessId, activeA.ProcessId);
        Assert.Equal(second.ProcessId, activeB.ProcessId);
        var journal = new JournalStore(root);
        var socialReceiptCountBeforeConcurrentIngress = (await journal.ReadReceiptIdsAsync(
            nameof(SocialPostObserved),
            TestContext.Current.CancellationToken)).Count;
        await Task.WhenAll(
            activeA.FireTrustedAsync(
                owners.SessionFor("owner-a"),
                new SocialPostObserved("after-handoff-a", "elonmusk", DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken),
            activeB.FireTrustedAsync(
                owners.SessionFor("owner-b"),
                new SocialPostObserved("after-handoff-b", "elonmusk", DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken));
        var concurrentReceipts = await journal.ReadReceiptIdsAsync(
            nameof(SocialPostObserved),
            TestContext.Current.CancellationToken);
        Assert.Equal(socialReceiptCountBeforeConcurrentIngress + 2, concurrentReceipts.Count);
        var durableAfterHandoff = await activeA.JournalKindsAsync(
            owners.SessionFor("owner-a"),
            TestContext.Current.CancellationToken);
        Assert.All(durableBefore, kind => Assert.Contains(kind, durableAfterHandoff));

        var coldRestart = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(coldRestart.Succeeded);
        Assert.NotEqual(second.ProcessId, coldRestart.ProcessId);
        await Assert.ThrowsAsync<HostQuiescingException>(() => activeA.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("rejected-cold-restart-attachment", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await activeA.WaitForExitAsync(TestContext.Current.CancellationToken);
        var restartedA = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var restartedB = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-b"),
            familyB,
            TestContext.Current.CancellationToken);
        Assert.Equal(coldRestart.ProcessId, restartedA.ProcessId);
        Assert.Equal(coldRestart.ProcessId, restartedB.ProcessId);
        await restartedA.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("after-cold-restart-a", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        await restartedB.FireTrustedAsync(
            owners.SessionFor("owner-b"),
            new SocialPostObserved("after-cold-restart-b", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        var durableAfterRestart = await restartedA.JournalKindsAsync(
            owners.SessionFor("owner-a"),
            TestContext.Current.CancellationToken);
        Assert.All(durableBefore, kind => Assert.Contains(kind, durableAfterRestart));

        Console.WriteLine(
            $"Task 7 two-owner PID evidence: old={first.ProcessId}, active={second.ProcessId}, cold={coldRestart.ProcessId}");
    }
}
