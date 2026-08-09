using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Charting;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class ColdRestartFacts
{
    private readonly TestOwnerAuthority _owners = new();

    [Fact]
    public async Task NewHostProcessRestoresStateAndCommittedOutbox()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var first = await HostProcess.StartAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken);
        await first.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new IncrementAndEmit(),
            TestContext.Current.CancellationToken);
        var firstPid = first.ProcessId;

        await first.TerminateAsync();

        await using var second = await HostProcess.StartAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken);
        var snapshot = await second.ReadSnapshotAsync(TestContext.Current.CancellationToken);

        Console.WriteLine(
            $"Task 3 restart PID evidence: first={firstPid}, second={second.ProcessId}");
        Assert.NotEqual(firstPid, second.ProcessId);
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.Equal(1, snapshot.CommittedOutboxCount);
        Assert.Contains("Emitted", snapshot.JournalKinds);
    }

    [Fact]
    public async Task HandlerFailureAndOversizedStateLeaveNoAcknowledgedTurnOrOutbox()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var host = await HostProcess.StartAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken);
        var session = _owners.SessionFor("owner-a");

        await Assert.ThrowsAsync<ProbeFailureException>(() => host.FireTrustedAsync(
            session,
            new ThrowAfterStateAndEmit(),
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<RemoteStateTooLargeException>(() => host.FireTrustedAsync(
            session,
            new ReplaceProbeState(new string('x', 65_537)),
            TestContext.Current.CancellationToken));

        var snapshot = await host.ReadSnapshotAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, snapshot.AcceptedCount);
        Assert.Equal(0, snapshot.CommittedOutboxCount);
        Assert.Empty(snapshot.JournalKinds);
    }

    [Fact]
    public async Task VerifiedCandidateLocalSynapseCrossesActivationAndRuntimeBoundary()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var family = CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc");
        var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            family,
            TestContext.Current.CancellationToken);
        await using var host = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);

        await host.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new ProbeIngress("probe-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, await host.ReadHandledCountAsync(
            candidate.Manifest.Contract(candidate.LocalSynapseAlias),
            TestContext.Current.CancellationToken));
        Assert.Equal(
            ["ProbeIngress", "ProbeSynapse"],
            await host.JournalKindsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewFixtureProcessDrainsCommittedCandidateOutboxAfterProducerCrash()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
            TestContext.Current.CancellationToken);
        await using var first = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);
        await first.StageTrustedBeforeDrainAsync(
            _owners.SessionFor("owner-a"),
            new ProbeIngress("recover-outbox-1"),
            TestContext.Current.CancellationToken);
        var firstPid = first.ProcessId;
        Assert.Equal(0, await first.ReadHandledCountAsync(
            candidate.LocalSynapseAlias,
            TestContext.Current.CancellationToken));
        var persisted = await first.ReadPersistedCandidatePayloadAsync(
            _owners.SessionFor("owner-a"),
            TestContext.Current.CancellationToken);
        Assert.Equal("recover-outbox-1", persisted.ProbeId);
        Assert.Equal(candidate.LocalSynapseAlias, persisted.ContractAlias);
        Assert.True(persisted.SerializedByteCount > 0);

        await first.TerminateAsync();

        await using var second = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);

        Assert.NotEqual(firstPid, second.ProcessId);
        Assert.Equal(1, await second.ReadHandledCountAsync(
            candidate.LocalSynapseAlias,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NewFixtureProcessDrainsCommittedTrustedChartTargetAfterProducerCrash()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildChartPointCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_dddddddddddddddddddddddddd"),
            "owner-a",
            ["elon-chart"],
            TestContext.Current.CancellationToken);
        await using var first = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);

        await first.StageTrustedBeforeDrainAsync(
            _owners.SessionFor("owner-a"),
            new ProbeIngress("post-recovered"),
            TestContext.Current.CancellationToken);
        var firstPid = first.ProcessId;
        await first.TerminateAsync();

        Assert.Single(await new Outbox(stateRoot).ReadPendingAsync(TestContext.Current.CancellationToken));
        var persisted = await new RunStore(stateRoot).ReadAsync(
            document => Assert.Single(document.Outbox),
            TestContext.Current.CancellationToken);
        Assert.Null(persisted.TargetRevision);
        Assert.Null(persisted.TargetModuleIdentity);
        Assert.Equal("elon-chart", persisted.TargetScope);
        Assert.Equal("json", persisted.PayloadFormat);

        await using var second = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);
        var chart = new ChartNeuron(new DurableTurn(stateRoot), "owner-a", "elon-chart");
        var facts = await new JournalStore(stateRoot).FindAsync<ChartPointAdded>(
            TestContext.Current.CancellationToken);
        var fact = Assert.Single(facts);

        Assert.NotEqual(firstPid, second.ProcessId);
        Assert.Equal([1], (await chart.ReadAsync(TestContext.Current.CancellationToken))
            .Points.Select(point => point.Ordinal));
        Assert.Equal(persisted.DeliveryId, fact.EffectId);
        Assert.Empty(await new Outbox(stateRoot).ReadPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadedCandidateActivationReceivesOnlyManifestGrantedChartTargets()
    {
        await using var grantedRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var granted = await CandidateFixtures.BuildChartPointCandidateAsync(
            grantedRoot,
            CandidateFamilyId.Parse("cf_eeeeeeeeeeeeeeeeeeeeeeeeee"),
            "owner-a",
            ["elon-chart"],
            TestContext.Current.CancellationToken);
        await using (var host = await HostProcess.StartVerifiedFixtureAsync(
            grantedRoot,
            _owners,
            TestContext.Current.CancellationToken,
            granted))
        {
            await host.FireTrustedAsync(
                _owners.SessionFor("owner-a"),
                new ProbeIngress("post-granted"),
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(
            [1],
            (await new ChartNeuron(new DurableTurn(grantedRoot), "owner-a", "elon-chart")
                .ReadAsync(TestContext.Current.CancellationToken))
            .Points.Select(point => point.Ordinal));

        await using var ungrantedRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var ungranted = await CandidateFixtures.BuildChartPointCandidateAsync(
            ungrantedRoot,
            CandidateFamilyId.Parse("cf_ffffffffffffffffffffffffff"),
            "owner-a",
            ["other-chart"],
            TestContext.Current.CancellationToken);
        await using var ungrantedHost = await HostProcess.StartVerifiedFixtureAsync(
            ungrantedRoot,
            _owners,
            TestContext.Current.CancellationToken,
            ungranted);

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => ungrantedHost.FireTrustedAsync(
            _owners.SessionFor("owner-a"),
            new ProbeIngress("post-ungranted"),
            TestContext.Current.CancellationToken));
        Assert.Empty(await new Outbox(ungrantedRoot).ReadPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoadedCandidateActivationCannotRouteToAForeignOwnerChart()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildChartPointCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_gggggggggggggggggggggggggg"),
            "owner-b",
            ["elon-chart"],
            TestContext.Current.CancellationToken);
        await using var host = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);

        await Assert.ThrowsAsync<CapabilityDeniedException>(() => host.FireTrustedAsync(
            _owners.SessionFor("owner-b"),
            new ProbeIngress("post-foreign"),
            TestContext.Current.CancellationToken));

        Assert.Empty((await new ChartNeuron(new DurableTurn(stateRoot), "owner-a", "elon-chart")
            .ReadAsync(TestContext.Current.CancellationToken)).Points);
        Assert.Single(await new Outbox(stateRoot).ReadPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestartRejectsChangedModuleBytesForAnExistingPinnedRevision()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var family = CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc");
        var original = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            family,
            TestContext.Current.CancellationToken);
        await using (var first = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            original))
        {
            await first.StageTrustedBeforeDrainAsync(
                _owners.SessionFor("owner-a"),
                new ProbeIngress("immutable-revision"),
                TestContext.Current.CancellationToken);
            await first.TerminateAsync();
        }

        var changed = await CandidateFixtures.BuildChangedProbeCandidateAsync(
            stateRoot,
            family,
            TestContext.Current.CancellationToken);

        var exception = await CaptureFixtureRejectionAsync(() =>
            HostProcess.StartVerifiedFixtureAsync(
                stateRoot,
                _owners,
                TestContext.Current.CancellationToken,
                changed));
        Assert.Contains("immutable module identity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixtureRejectsTheSameFamilyAcrossDifferentOwners()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
            TestContext.Current.CancellationToken);
        var duplicate = candidate with
        {
            Module = candidate.Module with { OwnerId = "owner-b" },
        };

        var exception = await CaptureFixtureRejectionAsync(() =>
            HostProcess.StartVerifiedFixtureAsync(
                stateRoot,
                _owners,
                TestContext.Current.CancellationToken,
                candidate,
                duplicate));
        Assert.Contains("globally unique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixtureRejectsTheSameFamilyAtDifferentRevisionsAcrossOwners()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
            TestContext.Current.CancellationToken);
        var duplicate = candidate with
        {
            Module = candidate.Module with { OwnerId = "owner-b", Revision = "revision-2" },
        };

        var exception = await CaptureFixtureRejectionAsync(() =>
            HostProcess.StartVerifiedFixtureAsync(
                stateRoot,
                _owners,
                TestContext.Current.CancellationToken,
                candidate,
                duplicate));
        Assert.Contains("globally unique", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerBCannotInvokeOwnerACandidateRoute()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
            TestContext.Current.CancellationToken);
        await using var host = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            candidate);

        await Assert.ThrowsAsync<AuthorizationException>(() => host.FireTrustedAsync(
            _owners.SessionFor("owner-b"),
            new ProbeIngress("probe-1"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NormalHostRejectsTheZeroArgumentScenarioMode()
    {
        var executable = HostProcess.FindNormalHostExecutable();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        }) ?? throw new InvalidOperationException("Could not start the normal POC host.");

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains(
            "requires trusted state and control-plane roots",
            await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerInputFansOutOnceToEachGrantedCandidateFamily()
    {
        await using var stateRoot = PocDataRoot.Create(HostProcess.FindPocRoot());
        var first = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
            TestContext.Current.CancellationToken);
        var second = await CandidateFixtures.BuildProbeCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb"),
            TestContext.Current.CancellationToken);
        var excluded = await CandidateFixtures.BuildOtherTriggerCandidateAsync(
            stateRoot,
            CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
            TestContext.Current.CancellationToken);
        await using var host = await HostProcess.StartVerifiedFixtureAsync(
            stateRoot,
            _owners,
            TestContext.Current.CancellationToken,
            first,
            second,
            excluded);

        var ownerSession = _owners.SessionFor("owner-a");
        await host.FireTrustedAsync(
            ownerSession,
            new ProbeIngress("fanout-1"),
            TestContext.Current.CancellationToken);
        await host.FireTrustedAsync(
            ownerSession,
            new ProbeIngress("fanout-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, await host.ReadHandledCountAsync(
            first.Manifest.Contract(first.LocalSynapseAlias),
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await host.ReadHandledCountAsync(
            second.Manifest.Contract(second.LocalSynapseAlias),
            TestContext.Current.CancellationToken));
        Assert.Equal(2, await host.ReadTurnCountAsync(
            first.Family,
            TestContext.Current.CancellationToken));
        Assert.Equal(2, await host.ReadTurnCountAsync(
            second.Family,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await host.ReadTurnCountAsync(
            excluded.Family,
            TestContext.Current.CancellationToken));
        Assert.NotEqual(first.LocalSynapseAlias, second.LocalSynapseAlias);
    }

    [Fact]
    public async Task DisposingAPocRunErasesHostAndCandidateEvidence()
    {
        var pocRoot = HostProcess.FindPocRoot();
        var stateRoot = PocDataRoot.Create(pocRoot);
        var runId = stateRoot.RunId;
        var rootPath = stateRoot.RootPath;
        try
        {
            var candidate = await CandidateFixtures.BuildProbeCandidateAsync(
                stateRoot,
                CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc"),
                TestContext.Current.CancellationToken);
            await using (var host = await HostProcess.StartVerifiedFixtureAsync(
                stateRoot,
                _owners,
                TestContext.Current.CancellationToken,
                candidate))
            {
                await host.FireTrustedAsync(
                    _owners.SessionFor("owner-a"),
                    new ProbeIngress("delete-me"),
                    TestContext.Current.CancellationToken);
            }

            await stateRoot.DisposeAsync();

            Assert.Empty(await PocDataRoot.FindArtifactsForRunAsync(
                pocRoot,
                runId,
                TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(rootPath));
        }
        finally
        {
            await stateRoot.DisposeAsync();
        }
    }

    private static async Task<InvalidOperationException> CaptureFixtureRejectionAsync(
        Func<Task<HostProcess>> start)
    {
        try
        {
            await using var host = await start();
            Assert.Fail("The fixture host unexpectedly accepted an invalid module set.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The fixture rejection helper did not produce a result.");
    }
}
