using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Gateway;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Hosting.Tests;

/// <summary>
/// Locks the three new gateway methods added in Inspector E.3 (Slice 3B):
/// <see cref="IInoGateway.ListProposalsAsync"/>,
/// <see cref="IInoGateway.DecideProposalAsync"/>,
/// <see cref="IInoGateway.ListRoutingDecisionsAsync"/>.
///
/// Tests target <see cref="InoGateway"/> directly (the transport-neutral layer)
/// rather than the thin gRPC wrapper, which is covered by build-time proto
/// codegen validation.
/// </summary>
public sealed class InoGrpcServiceInspectorRpcsTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    static InoGateway MakeGateway(IGrainFactory grainFactory) =>
        new(firePort: Substitute.For<IFirePort>(),
            events: Substitute.For<IInoEventBus>(),
            journal: new InMemorySynapseJournal(),
            reasoningProbe: new InMemoryReasoningProbe(),
            grainFactory: grainFactory,
            log: NullLogger<InoGateway>.Instance);

    static ProposalEntry FakeProposal(string proposalId, string userId, ProposalStatus status = ProposalStatus.Pending) =>
        new(ProposalId: proposalId,
            UserId: userId,
            ClusterKey: "cluster-x",
            ExamplePrompt: "do thing",
            AllPrompts: new[] { "do thing" },
            Occurrences: 3,
            ProposedAt: DateTimeOffset.UtcNow,
            Status: status,
            ActivatedNeuronId: null,
            DecidedAt: null,
            DecidedBy: null);

    static RoutingDecision FakeDecision(string prompt) =>
        new(Prompt: prompt,
            Source: RoutingSource.Regex,
            NeuronId: "test.exp",
            Confidence: 1.0,
            At: DateTimeOffset.UtcNow,
            MlPrediction: null,
            MlConfidence: null,
            LlmCalled: false,
            RoutingDurationMs: 1,
            CorrelationId: "corr-1");

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListProposalsAsync_passes_filter_through_and_scopes_to_user()
    {
        // Arrange
        var grainFactory = Substitute.For<IGrainFactory>();
        var proposalLog = Substitute.For<IProposalLog>();
        grainFactory.GetGrain<IProposalLog>("singleton").Returns(proposalLog);
        proposalLog
            .ListAsync(ProposalStatus.Pending, 0, 50)
            .ReturnsForAnyArgs(new ProposalEntry[]
            {
                FakeProposal("p1", "u1", ProposalStatus.Pending),
                FakeProposal("p2", "u2", ProposalStatus.Pending), // different user — must be excluded
            });

        var gateway = MakeGateway(grainFactory);

        // Act
        var result = await gateway.ListProposalsAsync(
            "u1", ProposalStatus.Pending, 0, 50, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("p1", result[0].ProposalId);
    }

    [Fact]
    public async Task DecideProposalAsync_Approve_calls_registry_ApproveAsync()
    {
        // Arrange
        var grainFactory = Substitute.For<IGrainFactory>();
        var registryGrain = Substitute.For<INeuronRegistry>();
        grainFactory.GetGrain<INeuronRegistry>(0L).Returns(registryGrain);
        registryGrain.ApproveAsync("p1", "u1", Arg.Any<CancellationToken>()).Returns(true);

        var gateway = MakeGateway(grainFactory);

        // Act — should not throw
        await gateway.DecideProposalAsync(
            "u1", "p1", ProposalStatus.Approved, TestContext.Current.CancellationToken);

        // Assert
        await registryGrain.Received(1).ApproveAsync("p1", "u1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListRoutingDecisionsAsync_caps_count_at_20()
    {
        // Arrange
        var grainFactory = Substitute.For<IGrainFactory>();
        var journal = Substitute.For<ICortexJournal>();
        grainFactory.GetGrain<ICortexJournal>("singleton").Returns(journal);
        journal
            .GetRecentAsync("u1", 20)
            .Returns(new RoutingDecision[] { FakeDecision("p") });

        var gateway = MakeGateway(grainFactory);

        // Act — request more than the cap
        var result = await gateway.ListRoutingDecisionsAsync(
            "u1", count: 100, TestContext.Current.CancellationToken);

        // Assert: grain was called with cap=20, not the caller's 100
        await journal.Received(1).GetRecentAsync("u1", 20);
        Assert.Single(result);
    }
}
