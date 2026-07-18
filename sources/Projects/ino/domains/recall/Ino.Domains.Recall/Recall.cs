using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Recall.Contracts;

namespace Ino.Domains.Recall;

/// <summary>
/// The Recall domain. Second IAW→ino capability bridge: surfaces semantic
/// memory (IAW's Qdrant-backed <c>IawMemoryProvider</c>) as the
/// <c>recall.search</c> neuron. The <c>RecallPlan</c> extracts a
/// question from the prompt and fires <see cref="RecallQuestion"/>;
/// <c>RecallNeuron</c> handles it by calling
/// <c>IMemoryLookup.LookupOriginAsync</c> against the per-user collection.
///
/// Auto-store ("every chat turn lands in the user's Qdrant collection") is
/// a follow-up — for v0.1 the demo seeds the collection out-of-band (or
/// inherits whatever IAW's chat pipeline already wrote there).
/// </summary>
public sealed class Recall : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Recall");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Fast),
    ];

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
    [
        new NeuronDefinition(
            NeuronId.From("recall.search"),
            DisplayName: "Recall what I told you",
            Description: "Look up something the user said in a previous conversation by semantic similarity.",
            CanonicalSynapseType: typeof(RecallQuestion),
            PromptExamples: [
                "what did I tell you about my mum's birthday",
                "do you remember where I parked the car",
                "what did I say about lisbon"
            ])
        {
            PlanType = typeof(IRecallPlan),
        },
    ];
}
