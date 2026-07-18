using Ino.Domains.Genesis.Neurons;
using Ino.Kernel.Contracts;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Unit-level coverage for <see cref="CreatorNeuron"/>'s pure helpers —
/// neuron-id slugging and draft-body templating. Behavioural coverage
/// of the full ReactAsync flow lives in
/// <see cref="L1LoopAcceptanceTests"/>, which spins up the test silo so
/// the registry + Discovery integration are exercised end-to-end.
/// </summary>
public sealed class CreatorNeuronTests
{
    static L1Proposal Proposal(string clusterKey, string proposalId = "01HZZ", int occurrences = 3) =>
        new(
            ProposalId: proposalId,
            UserId: "u-1",
            ClusterKey: clusterKey,
            ExamplePrompt: clusterKey,
            Occurrences: occurrences,
            ProposedAt: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("remind me to call mom", "genesis.remind-me-to-call-mom")]
    [InlineData("ORDER PIZZA", "genesis.order-pizza")]
    [InlineData("translate to french", "genesis.translate-to-french")]
    public void DraftNeuronId_slugs_the_cluster_key(string clusterKey, string expected)
    {
        Assert.Equal(expected, CreatorNeuron.DraftNeuronId(Proposal(clusterKey)));
    }

    [Fact]
    public void DraftNeuronId_truncates_long_keys()
    {
        var clusterKey = new string('a', 64);
        var id = CreatorNeuron.DraftNeuronId(Proposal(clusterKey));
        // "genesis." (8) + 32-char slug = 40 chars max
        Assert.Equal("genesis." + new string('a', 32), id);
    }

    [Fact]
    public void DraftNeuronId_falls_back_to_proposal_id_when_slug_is_empty()
    {
        var id = CreatorNeuron.DraftNeuronId(Proposal("!!!", proposalId: "01HABC"));
        Assert.Equal("genesis.01habc", id);
    }

    [Fact]
    public void DraftScriptBody_compiles_against_PlanCompiler()
    {
        var body = CreatorNeuron.DraftScriptBody(Proposal("translate to french", occurrences: 5));
        Assert.Null(Compilation.PlanCompiler.Validate(body));
    }

    [Fact]
    public void DraftScriptBody_includes_occurrence_count()
    {
        var body = CreatorNeuron.DraftScriptBody(Proposal("test prompt", occurrences: 7));
        Assert.Contains("7 unrouted prompts", body);
    }
}
