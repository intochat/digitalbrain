using System.Net;
using DigitalBrain.Poc.Charting;
using DigitalBrain.Poc.Charting.Contracts;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class ChartNeuronFacts : IAsyncLifetime
{
    private PocDataRoot _root = null!;
    private DurableTurn _turns = null!;
    private TestOwnerAuthority _owners = null!;
    private ChartNeuron _elonChart = null!;
    private ChartNeuron _ownerAChart = null!;
    private ChartProjectionEndpoint _charts = null!;
    private BrainFacade _brain = null!;

    public ValueTask InitializeAsync()
    {
        _root = PocDataRoot.Create(TestPocRoot.Find());
        _turns = new DurableTurn(_root);
        _owners = new TestOwnerAuthority();
        _elonChart = new ChartNeuron(_turns, "owner-a", "elon-chart");
        _ownerAChart = new ChartNeuron(_turns, "owner-a", "owner-a-chart");
        _charts = new ChartProjectionEndpoint([_elonChart, _ownerAChart]);
        _brain = new BrainFacade(DeliverToGrantedChartAsync);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    [Fact]
    public async Task TrustedChartNeuronPersistsOnePointAndPublishesOneTerminalFact()
    {
        await FireAsync(
            "owner-a",
            "input-1",
            new AddChartPoint(
                "elon-chart",
                new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z"))));

        var snapshot = await _charts.ReadAsync(
            "owner-a",
            "elon-chart",
            TestContext.Current.CancellationToken);
        var facts = await new JournalStore(_root).FindAsync<ChartPointAdded>(
            TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal([1], snapshot.Points.Select(point => point.Ordinal));
        var fact = Assert.Single(facts);
        Assert.Equal("post-1", fact.Point.SourcePostId);
        Assert.Equal(
            fact.EffectId,
            (await new JournalStore(_root).ReadReceiptIdsAsync(
                "ChartPointAdded",
                TestContext.Current.CancellationToken)).Single());
        Assert.Empty(await new Outbox(_root).ReadCommittedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplayedEffectIdDoesNotDuplicateAChartPoint()
    {
        var input = new AddChartPoint(
            "elon-chart",
            new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z")));

        await FireAsync("owner-a", "input-1", input);
        await FireAsync("owner-a", "input-1", input);

        Assert.Single((await _charts.ReadAsync(
            "owner-a",
            "elon-chart",
            TestContext.Current.CancellationToken))!.Points);
        Assert.Single(await new JournalStore(_root).FindAsync<ChartPointAdded>(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DurableNextOrdinalContinuesAfterChartNeuronReconstruction()
    {
        await FireAsync(
            "owner-a",
            "input-1",
            new AddChartPoint(
                "elon-chart",
                new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z"))));
        var reconstructed = new ChartNeuron(_turns, "owner-a", "elon-chart");
        var reconstructedBrain = new BrainFacade(
            envelope => reconstructed.HandleAsync(envelope, TestContext.Current.CancellationToken));

        await reconstructedBrain.ForCandidate(
                Scope("owner-a", "input-2"),
                [typeof(AddChartPoint)],
                ["elon-chart"])
            .FireSynapse(
                new AddChartPoint(
                    "elon-chart",
                    new ChartPointDraft("post-2", DateTimeOffset.Parse("2026-08-09T10:01:00Z"))),
                TestContext.Current.CancellationToken);

        Assert.Equal(
            [1, 2],
            (await reconstructed.ReadAsync(TestContext.Current.CancellationToken))
                .Points.Select(point => point.Ordinal));
    }

    [Fact]
    public async Task TrustedChartTargetSurvivesTheDurableOutboxWithoutCandidateRouting()
    {
        var command = new AddChartPoint(
            "elon-chart",
            new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z")));
        SynapseEnvelope? captured = null;
        var scope = Scope("owner-a", "input-persisted-target");
        var capturingBrain = new BrainFacade(envelope =>
        {
            captured = envelope;
            return Task.CompletedTask;
        });
        await capturingBrain.ForCandidate(
                scope,
                [typeof(AddChartPoint)],
                ["elon-chart"])
            .FireSynapse(command, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        await _turns.ExecuteAsync(
            "upstream-turn",
            "ElonPostMatched",
            "upstream-state",
            0,
            scope.OwnerId,
            scope.Family,
            scope.Revision,
            scope.ModuleIdentity,
            targetRevision: null,
            targetModuleIdentity: null,
            handledCountKey: null,
            familyHandledCountKey: null,
            journalInput: true,
            envelopeAt: _ => captured,
            serializeCandidatePayload: _ => throw new InvalidOperationException(
                "A trusted chart contract must use the host-owned JSON path."),
            async (_, stagedBrain) =>
                await stagedBrain.FireSynapse(command, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);

        var stored = await new RunStore(_root).ReadAsync(
            document => Assert.Single(document.Outbox),
            TestContext.Current.CancellationToken);
        Assert.Null(stored.TargetRevision);
        Assert.Equal("elon-chart", stored.TargetScope);
        Assert.Equal("json", stored.PayloadFormat);

        var freshTurns = new DurableTurn(_root);
        var freshChart = new ChartNeuron(freshTurns, "owner-a", "elon-chart");
        var freshCharts = new ChartProjectionEndpoint([freshChart]);
        var freshDrain = new TrustedChartOutboxDrain(freshTurns, freshCharts);

        await freshDrain.DrainAsync(TestContext.Current.CancellationToken);
        await freshDrain.DrainAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [1],
            (await freshChart.ReadAsync(TestContext.Current.CancellationToken))
            .Points.Select(point => point.Ordinal));
        Assert.Single(await new JournalStore(_root).FindAsync<ChartPointAdded>(
            TestContext.Current.CancellationToken));
        Assert.Empty(await new Outbox(_root).ReadPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeduplicatedTrustedChartDeliveryDoesNotInvokeThePostCommitHook()
    {
        var command = new AddChartPoint(
            "elon-chart",
            new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z")));
        SynapseEnvelope? captured = null;
        var scope = Scope("owner-a", "input-deduplicated-target");
        var capturingBrain = new BrainFacade(envelope =>
        {
            captured = envelope;
            return Task.CompletedTask;
        });
        await capturingBrain.ForCandidate(
                scope,
                [typeof(AddChartPoint)],
                ["elon-chart"])
            .FireSynapse(command, TestContext.Current.CancellationToken);

        await _turns.ExecuteAsync(
            "upstream-deduplicated-turn",
            "ElonPostMatched",
            "upstream-deduplicated-state",
            0,
            scope.OwnerId,
            scope.Family,
            scope.Revision,
            scope.ModuleIdentity,
            targetRevision: null,
            targetModuleIdentity: null,
            handledCountKey: null,
            familyHandledCountKey: null,
            journalInput: true,
            envelopeAt: _ => captured,
            serializeCandidatePayload: _ => throw new InvalidOperationException(
                "A trusted chart contract must use the host-owned JSON path."),
            async (_, stagedBrain) =>
                await stagedBrain.FireSynapse(command, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        await new TrustedChartOutboxDrain(_turns, _charts).DrainAsync(
            TestContext.Current.CancellationToken);
        var deliveryId = await new RunStore(_root).ReadAsync(
            document =>
            {
                var entry = Assert.Single(document.Outbox);
                Assert.True(entry.Delivered);
                Assert.Contains(entry.DeliveryId, document.AcknowledgedReceipts);
                return entry.DeliveryId;
            },
            TestContext.Current.CancellationToken);
        await new RunStore(_root).TransactAsync(
            document =>
            {
                var index = document.Outbox.FindIndex(entry => entry.DeliveryId == deliveryId);
                document.Outbox[index] = document.Outbox[index] with { Delivered = false };
                return Task.FromResult((true, true));
            },
            TestContext.Current.CancellationToken);

        var faultFired = false;
        var freshTurns = new DurableTurn(_root);
        var freshChart = new ChartNeuron(freshTurns, "owner-a", "elon-chart");
        var freshCharts = new ChartProjectionEndpoint([freshChart]);
        Assert.False(await freshCharts.DeliverTrustedTargetWithCommitAsync(
            SynapseEnvelope.RestoreTrustedTarget(
                deliveryId,
                scope.OwnerId,
                ContractAlias.For(typeof(AddChartPoint)),
                command,
                scope.Family,
                scope.Revision,
                scope.ModuleIdentity,
                "elon-chart"),
            TestContext.Current.CancellationToken));
        var freshDrain = new TrustedChartOutboxDrain(
            freshTurns,
            freshCharts,
            _ =>
            {
                faultFired = true;
                return Task.CompletedTask;
            });

        await freshDrain.DrainAsync(TestContext.Current.CancellationToken);

        Assert.False(faultFired);
        Assert.Equal(
            [1],
            (await freshChart.ReadAsync(TestContext.Current.CancellationToken))
                .Points.Select(point => point.Ordinal));
        Assert.Empty(await new Outbox(_root).ReadPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ForeignOwnerOrUngrantedTargetCannotMutateAChart()
    {
        var ungranted = _brain.ForCandidate(
            Scope("owner-b", "input-ungranted"),
            [typeof(AddChartPoint)],
            ["owner-b-chart"]);

        await Assert.ThrowsAsync<CapabilityDeniedException>(
            () => ungranted.FireSynapse(
                new AddChartPoint(
                    "owner-a-chart",
                    new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z"))),
                TestContext.Current.CancellationToken));

        var wronglyGrantedForeignTarget = _brain.ForCandidate(
            Scope("owner-b", "input-foreign"),
            [typeof(AddChartPoint)],
            ["owner-a-chart"]);
        await Assert.ThrowsAsync<CapabilityDeniedException>(
            () => wronglyGrantedForeignTarget.FireSynapse(
                new AddChartPoint(
                    "owner-a-chart",
                    new ChartPointDraft("post-1", DateTimeOffset.Parse("2026-08-09T10:00:00Z"))),
                TestContext.Current.CancellationToken));

        Assert.Empty((await _charts.ReadAsync(
            "owner-a",
            "owner-a-chart",
            TestContext.Current.CancellationToken))!.Points);
    }

    [Fact]
    public async Task ProjectionEndpointRejectsForgedOwnerToken()
    {
        var response = await ChartProjectionRoutes.GetAsync(
            bearerToken: "owner-a",
            chartId: "elon-chart",
            _owners,
            _charts,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProjectionEndpointHidesAnotherOwnersChart()
    {
        var response = await ChartProjectionRoutes.GetAsync(
            bearerToken: _owners.SessionFor("owner-b").OpaqueToken,
            chartId: "owner-a-chart",
            _owners,
            _charts,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Task FireAsync(string ownerId, string inputDeliveryId, AddChartPoint input) =>
        _brain.ForCandidate(
                Scope(ownerId, inputDeliveryId),
                [typeof(AddChartPoint)],
                [input.ChartId])
            .FireSynapse(input, TestContext.Current.CancellationToken);

    private Task DeliverToGrantedChartAsync(SynapseEnvelope envelope) =>
        envelope.TargetScope switch
        {
            "elon-chart" => _elonChart.HandleAsync(envelope, TestContext.Current.CancellationToken),
            "owner-a-chart" => _ownerAChart.HandleAsync(envelope, TestContext.Current.CancellationToken),
            _ => throw new InvalidOperationException($"No trusted chart exists for '{envelope.TargetScope}'."),
        };

    private static CandidateInvocationScope Scope(string ownerId, string inputDeliveryId) =>
        new(
            ownerId,
            CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
            "revision-1",
            new CandidateModuleIdentity(
                new string('a', 64),
                new string('b', 64),
                new string('c', 64)),
            inputDeliveryId);
}
