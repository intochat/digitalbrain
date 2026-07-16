using System.Text.Json;
using Brain.Contracts;

namespace Brain.Kernel;

public sealed class EffectKind : INeuronKind
{
    private static readonly JsonSerializerOptions ProofJsonOptions = new(JsonSerializerDefaults.Web);

    public string Kind => "effect";
    public string[] Contracts => ["effect.propose.v1", "effect.approve.v1", "effect.decline.v1", "effect.claim-proof.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "effect.propose.v1" => Propose(context, invocation.InputJson),
            "effect.approve.v1" => Decide(context, "approved"),
            "effect.decline.v1" => Decide(context, "declined"),
            "effect.claim-proof.v1" => ClaimProof(context),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection)
    {
        var state = FoldState(context.Journal);
        if (state == "empty")
            return $$"""{"state":"{{state}}"}""";

        var proposal = JsonSerializer.Deserialize<EffectProposal>(context.Journal[0].PayloadJson)!;
        return $$"""{"state":"{{state}}","provider":"{{proposal.Provider}}"}""";
    }

    private static ValueTask<KindResult> Propose(NeuronContext context, string proposalJson)
    {
        if (FoldState(context.Journal) != "empty")
            throw new BrainException(BrainErrors.EffectNotApproved, "effect already proposed");

        var caller = NeuronAddress.Parse(context.CallerKey);
        if (caller.OwnerId != context.Address.OwnerId || caller.NeuronId.StartsWith("effect/", StringComparison.Ordinal))
            throw new BrainException(BrainErrors.EffectNotApproved, "proposer not permitted");

        return ValueTask.FromResult(new KindResult(proposalJson, [("proposed", proposalJson)]));
    }

    private static ValueTask<KindResult> Decide(NeuronContext context, string eventKind)
    {
        if (FoldState(context.Journal) != "proposed")
            throw new BrainException(BrainErrors.EffectNotApproved, $"cannot {eventKind} from current state");

        return ValueTask.FromResult(new KindResult("{}", [(eventKind, "{}")]));
    }

    private static ValueTask<KindResult> ClaimProof(NeuronContext context)
    {
        if (FoldState(context.Journal) != "approved")
            throw new BrainException(BrainErrors.EffectNotApproved, "effect is not approved and unclaimed");

        var proposed = context.Journal.First(e => e.Kind == "proposed");
        var approved = context.Journal[^1];
        var proposal = JsonSerializer.Deserialize<EffectProposal>(proposed.PayloadJson)!;
        var proof = new ApprovedEffectProof(context.Address.ToGrainKey(), context.Revision, proposal.PayloadDigest, approved.CommandId);
        var proofJson = JsonSerializer.Serialize(proof, ProofJsonOptions);

        return ValueTask.FromResult(new KindResult(proofJson, [("claimed", proofJson)]));
    }

    private static string FoldState(IReadOnlyList<NeuronEvent> journal) =>
        journal.Count == 0 ? "empty" : journal[^1].Kind;
}
