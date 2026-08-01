using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class PublishedBehaviorDiscovery
{
    private const string BehaviorId = "behavior.account-enrichment";
    private const string ArtifactHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string StaleHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact(DisplayName = "publish attaches exact active behavior descriptor and stop removes it")]
    public void PublishAttachesAndStopRemovesExactBehaviorDescriptor()
    {
        var catalog = ActiveCapabilityCatalog.Create(Array.Empty<ICompiledModule>());
        catalog.PublishBehavior(Descriptor(ArtifactHash, ["enrich account from email"]));
        Assert.True(catalog.TryGetBehavior(BehaviorId, out var published));
        Assert.Equal(ArtifactHash, published!.ArtifactHash);
        Assert.Single(catalog.Behaviors);

        catalog.PublishBehavior(Descriptor(ArtifactHash, ["updated scenario"]));
        Assert.True(catalog.TryGetBehavior(BehaviorId, out var replaced));
        Assert.Equal(["updated scenario"], replaced!.ScenarioTitles);

        Assert.True(catalog.UnpublishBehavior(BehaviorId));
        Assert.False(catalog.TryGetBehavior(BehaviorId, out _));
        Assert.Empty(catalog.Behaviors);
    }

    [Fact(DisplayName = "vector behavior hits never execute an arbitrary artifact hash; exact catalog revision wins")]
    public async Task VectorArtifactHashNeverOverridesExactActiveRevision()
    {
        var catalog = ActiveCapabilityCatalog.Create(Array.Empty<ICompiledModule>());
        catalog.PublishBehavior(Descriptor(ArtifactHash, ["enrich account from email"]));

        var search = new ScriptedCandidateSearch(
        [
            new CapabilityCandidate(
                CapabilityKinds.Behavior,
                BehaviorId,
                SchemaVersion: 1,
                ModuleId: null,
                NeuronContractId: "behaviors.behavior",
                BehaviorId: BehaviorId,
                ArtifactHash: StaleHash,
                SourceKey: BehaviorId),
        ]);
        var router = new CapabilityRouter(catalog, search);

        var selected = await router.SelectAsync(
            new OwnerId("owner-a"),
            "enrich account from email",
            CancellationToken.None);

        var capability = Assert.Single(selected);
        Assert.Equal(BehaviorId, capability.BehaviorId);
        Assert.Equal(ArtifactHash, capability.ArtifactHash);
        Assert.NotEqual(StaleHash, capability.ArtifactHash);
    }

    [Fact(DisplayName = "draft private and stopped behaviors are absent from exact active behavior discovery")]
    public void DraftPrivateStoppedAreNotInExactCatalog()
    {
        var catalog = ActiveCapabilityCatalog.Create(Array.Empty<ICompiledModule>());
        Assert.False(catalog.TryGetBehavior("behavior.draft", out _));
        Assert.Empty(catalog.Behaviors);
    }

    private static ActiveBehaviorCapability Descriptor(string artifactHash, IReadOnlyList<string> scenarios)
        => new(
            BehaviorId,
            "Account enrichment",
            "Enrich a Salesforce account from a Gmail message.",
            artifactHash,
            instanceName: "account-enrichment",
            neuronContractId: "behaviors.behavior",
            jsonSchema: """{"type":"object","properties":{"triggerTypeName":{"type":"string"},"triggerJson":{"type":"string"}}}""",
            scenarioTitles: scenarios);

    private sealed class ScriptedCandidateSearch(IReadOnlyList<CapabilityCandidate> candidates) : ICapabilityCandidateSearch
    {
        public Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
            OwnerId owner,
            string prompt,
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult(candidates);
    }
}
