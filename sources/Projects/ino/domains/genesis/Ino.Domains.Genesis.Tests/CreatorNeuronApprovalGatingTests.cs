using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Genesis.Neurons;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Phase 4 epilogue Slice 3A: <see cref="CreatorNeuron"/> branches on
/// <see cref="INeuronRegistry.GetApprovalRequiredAsync"/>. When the
/// flag is true, <see cref="CreatorNeuron.ReactAsync"/> stashes the draft
/// and suppresses direct registration. When
/// <see cref="INeuronRegistry.ApproveAsync"/> is later called the
/// draft promotes to a live neuron.
///
/// Test 1 is a pure unit test with NSubstitute mocks.
/// Tests 2+3 are integration tests using the full Genesis cluster
/// (<see cref="GenesisTestSiloFixture"/>).
/// </summary>
public sealed class CreatorNeuronApprovalGatingUnitTests
{
    static NeuronContext Ctx(string userId = "u1") =>
        NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")),
            userId: userId);

    [Fact]
    public async Task Gating_on_stashes_draft_and_suppresses_registration()
    {
        var registry = Substitute.For<INeuronRegistry>();
        registry.GetApprovalRequiredAsync(Arg.Any<CancellationToken>()).Returns(true);

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<INeuronRegistry>(0).Returns(registry);

        var firePort = Substitute.For<IFirePort>();

        var sut = new CreatorNeuron(grainFactory, firePort, NullLogger<CreatorNeuron>.Instance);
        var proposal = new L1Proposal("p-gate-1", "u1", "order-a-pizza", "order a pizza", 3, DateTimeOffset.UtcNow);

        await sut.ReactAsync(proposal, Ctx(), CancellationToken.None);

        // Draft was stashed
        await registry.Received(1).StashDraftAsync(
            "p-gate-1", Arg.Any<DraftNeuron>(), Arg.Any<CancellationToken>());

        // Script body was NOT registered directly
        await registry.DidNotReceive().RegisterAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// Integration tests for approval gating — use the full Genesis cluster so
/// NeuronRegistry grain runs with real Orleans + real IFirePort.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class CreatorNeuronApprovalGatingIntegrationTests(GenesisTestSiloFixture fx)
{
    [Fact]
    public async Task ApproveAsync_promotes_draft_to_live_neuron()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = $"user-{Guid.NewGuid():n}";
        var proposalId = Ulid.NewUlid().ToString();

        // Drive CreatorNeuron — approval required = true by default in tests.
        var creator =fx.Grains.GetGrain<IReactsTo<L1Proposal>>(Guid.NewGuid().ToString("n"));
        var proposal = new L1Proposal(proposalId, userId, "order-pizza", "order pizza", 3, DateTimeOffset.UtcNow);
        var ctx = NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")), userId: userId);
        await creator.ReactAsync(proposal, ctx, ct);

        var registry = fx.Grains.GetGrain<INeuronRegistry>(0);

        // Draft is stashed — NOT yet registered
        var neuronId = CreatorNeuron.DraftNeuronId(proposal);
        var bodyBefore = await registry.GetScriptBodyAsync(neuronId, ct);
        Assert.Null(bodyBefore);

        // Approve: should register script body and return true
        var approved = await registry.ApproveAsync(proposalId, "admin-user", ct);
        Assert.True(approved);

        // Script body is now live
        var bodyAfter = await registry.GetScriptBodyAsync(neuronId, ct);
        Assert.NotNull(bodyAfter);

        // Second approve for same proposal returns false (no stash left)
        var secondApprove = await registry.ApproveAsync(proposalId, "admin-user", ct);
        Assert.False(secondApprove);
    }

    [Fact]
    public async Task RejectAsync_discards_stash_and_subsequent_approve_returns_false()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = $"user-{Guid.NewGuid():n}";
        var proposalId = Ulid.NewUlid().ToString();

        var creator =fx.Grains.GetGrain<IReactsTo<L1Proposal>>(Guid.NewGuid().ToString("n"));
        var proposal = new L1Proposal(proposalId, userId, "set-timer", "set a timer", 3, DateTimeOffset.UtcNow);
        var ctx = NeuronContextForTest.Create(
            source: new Caller.FromDomain(DomainId.From("kernel")), userId: userId);
        await creator.ReactAsync(proposal, ctx, ct);

        var registry = fx.Grains.GetGrain<INeuronRegistry>(0);

        var rejected = await registry.RejectAsync(proposalId, "admin-user", ct);
        Assert.True(rejected);

        // Subsequent approve should fail (stash was removed)
        var approved = await registry.ApproveAsync(proposalId, "admin-user", ct);
        Assert.False(approved);

        // Script body was never registered
        var neuronId = CreatorNeuron.DraftNeuronId(proposal);
        var body = await registry.GetScriptBodyAsync(neuronId, ct);
        Assert.Null(body);
    }
}
