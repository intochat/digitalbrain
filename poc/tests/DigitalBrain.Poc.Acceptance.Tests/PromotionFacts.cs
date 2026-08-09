using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using System.Security.Cryptography;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class PromotionFacts
{
    [Fact]
    public async Task CompatiblePromotionAndRollbackContinueOneStableNeuronState()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var owner = fixture.Owners.PrincipalForTest("owner-a");
        var session = fixture.Owners.SessionFor("owner-a");
        var family = ElonChartAuthoringIntent.DefaultTrustedFixture.Family;
        var first = await fixture.CreateApprovedTrustedFixtureAsync(
            owner,
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            TestContext.Current.CancellationToken);
        var second = await fixture.CreateApprovedTrustedFixtureAsync(
            owner,
            ElonChartAuthoringIntent.ForTrustedFixture(family, "elon-chart-next", "elonmusk"),
            TestContext.Current.CancellationToken);

        var firstHost = await fixture.PromoteAsync(
            owner,
            first.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        await firstHost.FireTrustedAsync(
            session,
            new SocialPostObserved("state-v1", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, await firstHost.GeneratedAcceptedCountAsync(
            session,
            family,
            TestContext.Current.CancellationToken));

        var secondHost = await fixture.PromoteAsync(
            owner,
            second.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        await secondHost.FireTrustedAsync(
            session,
            new SocialPostObserved("state-v2", "elonmusk", DateTimeOffset.UnixEpoch.AddMinutes(1)),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, await secondHost.GeneratedAcceptedCountAsync(
            session,
            family,
            TestContext.Current.CancellationToken));

        var rolledBack = await fixture.Supervisor.RollbackAsync(
            owner,
            family,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(rolledBack.Succeeded);
        await rolledBack.Attachment!.FireTrustedAsync(
            session,
            new SocialPostObserved("state-rollback", "elonmusk", DateTimeOffset.UnixEpoch.AddMinutes(2)),
            TestContext.Current.CancellationToken);
        Assert.Equal(3, await rolledBack.Attachment.GeneratedAcceptedCountAsync(
            session,
            family,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IngressAdmissionLeaseRegistersBeforeCloseAndTransfersToQueuedTurn()
    {
        var gate = new IngressQuiesceGate();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var lease = gate.Acquire(
            async (_, cancellationToken) =>
            {
                turnStarted.TrySetResult();
                await releaseTurn.Task.WaitAsync(cancellationToken);
            });

        gate.Close();
        await Assert.ThrowsAsync<HostQuiescingException>(() => Task.FromResult(
            gate.Acquire((_, _) => Task.CompletedTask)));
        var drain = gate.WaitForDrainAsync(TestContext.Current.CancellationToken);
        Assert.False(drain.IsCompleted);

        var fire = lease.FireAsync(new TestIngress(), TestContext.Current.CancellationToken);
        await turnStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(drain.IsCompleted);
        releaseTurn.TrySetResult();
        await fire;
        await drain;
    }

    [Fact]
    public async Task DisposingUnusedPreCloseLeaseAllowsDrainWithoutQueueing()
    {
        var gate = new IngressQuiesceGate();
        var lease = gate.Acquire((_, _) => throw new InvalidOperationException("must not queue"));
        gate.Close();
        var drain = gate.WaitForDrainAsync(TestContext.Current.CancellationToken);
        Assert.False(drain.IsCompleted);

        await lease.DisposeAsync();

        await drain;
    }

    [Fact]
    public async Task PromotionAndRollbackBothStartNewVerifiedProcesses()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(
            root,
            attestations,
            approvals,
            pointers);
        var catalog = new CandidateCatalog(store);
        var first = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var second = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                "elon-chart-next",
                "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await AttestAndApproveAsync(first, root, store, attestations, catalog, owners);
        await AttestAndApproveAsync(second, root, store, attestations, catalog, owners);
        await using var supervisor = new HostSupervisor(
            root,
            store,
            pointers,
            owners);

        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            first.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        var coldBoot = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(coldBoot.Succeeded);
        Assert.Equal(first.Id, coldBoot.ActiveSourceHash);
        Assert.NotEqual(second.Id, coldBoot.ActiveSourceHash);
        var priorHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        await using var preCloseLease = coldBoot.Attachment!.AcquireIngressLease(
            owners.SessionFor("owner-a"));
        var promotion = supervisor.BeginPromotionAsync(
            owners.PrincipalForTest("owner-a"),
            second.Id,
            HostFault.PauseAfterIngressClosedBeforeDrain,
            cancellationToken: TestContext.Current.CancellationToken);
        await supervisor.WaitUntilIngressClosedAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.False(promotion.IsCompleted);
        Assert.Equal(
            priorHead,
            await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HostQuiescingException>(() => coldBoot.Attachment.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("late-post", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        supervisor.ReleaseTestFault();
        Assert.False(promotion.IsCompleted);
        await preCloseLease.FireAsync(
            new SocialPostObserved("pre-close-post", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        var promoted = await promotion;
        var rolledBack = await supervisor.RollbackAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(initial.Succeeded);
        Assert.True(promoted.Succeeded);
        Assert.True(rolledBack.Succeeded);
        Assert.NotEqual(initial.ProcessId, promoted.ProcessId);
        Assert.NotEqual(promoted.ProcessId, rolledBack.ProcessId);
        Assert.Equal(first.Id, rolledBack.ActiveSourceHash);
        Assert.Equal(
            rolledBack.ProcessId,
            (await supervisor.CurrentAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken)).ProcessId);
        var rollbackHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        var childFailure = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            second.Id,
            HostFault.BeforeCandidateChildReady,
            TestContext.Current.CancellationToken);
        Assert.False(childFailure.Succeeded);
        Assert.Equal(
            rollbackHead,
            await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));

        var incompatible = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                ElonChartAuthoringIntent.DefaultTrustedFixture.AttestedTriggerAlias,
                "elon-chart-schema-v2",
                "elonmusk",
                localSynapseSchemaVersion: 2),
            root,
            TestContext.Current.CancellationToken);
        await AttestAndApproveAsync(
            incompatible,
            root,
            store,
            attestations,
            catalog,
            owners);
        var rejectedSchema = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            incompatible.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejectedSchema.Succeeded);
        Assert.Equal(PromotionFailure.IncompatibleRetainedSchema, rejectedSchema.Failure);
        Assert.Equal(
            rollbackHead,
            await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));

        var fanOutFault = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            second.Id,
            HostFault.AfterTrustedFanOutCommitBeforeRuleAcknowledgement,
            TestContext.Current.CancellationToken);
        Assert.True(fanOutFault.Succeeded);
        await Assert.ThrowsAnyAsync<Exception>(() => fanOutFault.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("fan-out-fault", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await fanOutFault.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(await new Outbox(root).PendingTargetingCandidateRevisionAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            second.Id,
            TestContext.Current.CancellationToken));
        var blockedRollback = await supervisor.RollbackAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(blockedRollback.Succeeded);
        Assert.Equal(PromotionFailure.PendingCandidateTargetedOutbox, blockedRollback.Failure);
        var drainedFanOut = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(drainedFanOut.Succeeded);
        Assert.Empty(await new Outbox(root).PendingTargetingCandidateRevisionAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            second.Id,
            TestContext.Current.CancellationToken));

        var backToFirst = await supervisor.RollbackAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(backToFirst.Succeeded);
        var localFault = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            second.Id,
            HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement,
            TestContext.Current.CancellationToken);
        Assert.True(localFault.Succeeded);
        await Assert.ThrowsAnyAsync<Exception>(() => localFault.Attachment!.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved("local-fault", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await localFault.Attachment!.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(await new Outbox(root).PendingTargetingCandidateRevisionAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            second.Id,
            TestContext.Current.CancellationToken));
        var blockedPromotion = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            incompatible.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(blockedPromotion.Succeeded);
        Assert.Equal(PromotionFailure.PendingCandidateTargetedOutbox, blockedPromotion.Failure);

        var drainedLocal = await supervisor.TryRestartActiveAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.True(drainedLocal.Succeeded);
        Assert.Empty(await new Outbox(root).PendingTargetingCandidateRevisionAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            second.Id,
            TestContext.Current.CancellationToken));
        var headBeforeTamper = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        File.SetAttributes(
            incompatible.AssemblyPath,
            File.GetAttributes(incompatible.AssemblyPath) & ~FileAttributes.ReadOnly);
        await File.AppendAllTextAsync(
            incompatible.AssemblyPath,
            "tamper",
            TestContext.Current.CancellationToken);
        var tampered = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            incompatible.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(tampered.Succeeded);
        Assert.Equal(PromotionFailure.CandidateVerificationFailed, tampered.Failure);
        Assert.Equal(
            headBeforeTamper,
            await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));

        Console.WriteLine(
            $"Task 7 restart PID evidence: initial={initial.ProcessId}, cold={coldBoot.ProcessId}, promoted={promoted.ProcessId}, rollback={rolledBack.ProcessId}");
    }

    internal static async Task AttestAndApproveAsync(
        FileCandidateCompiler.CompiledCandidate compiled,
        PocDataRoot root,
        TrustedCandidateCatalogStore store,
        AttestationSigner attestations,
        CandidateCatalog catalog,
        TestOwnerAuthority owners,
        string ownerId = "owner-a")
    {
        var manifest = compiled.Manifest;
        var payload = new CandidateAttestationPayload(
            compiled.Id,
            root.RunId,
            ownerId,
            compiled.Intent.Family.Value,
            manifest.SourceHash,
            manifest.AssemblyHash,
            manifest.CandidateMetadataHash,
            Convert.ToHexString(SHA256.HashData("task-7-scenario"u8)).ToLowerInvariant())
        {
            Revision = $"quarantine-{manifest.AssemblyHash}",
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
            ResolvedReferences = manifest.ResolvedReferences,
            NormalizedAstHash = manifest.NormalizedAstHash,
            FixedHeaderHash = manifest.FixedHeaderHash,
            CompilerHash = manifest.CompilerHash,
            SdkHash = manifest.SdkHash,
            ReferencesHash = manifest.ReferencesHash,
            CapabilitiesHash = manifest.CapabilitiesHash,
            ContractsHash = manifest.ContractsHash,
            StateSchemaHash = manifest.StateSchemaHash,
        };
        await store.WriteAttestationAsync(
            attestations.Sign(payload),
            TestContext.Current.CancellationToken);
        await catalog.ApproveAsync(
            owners.PrincipalForTest(ownerId),
            compiled.Id,
            TestContext.Current.CancellationToken);
    }

    private sealed record TestIngress : Synapse;
}
