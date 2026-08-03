using DigitalBrain.Abstractions;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Behaviors;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.AI;

[GrainType("behaviorauthoring")]
internal sealed class BehaviorAuthorNeuron :
    Neuron,
    IBehaviorAuthoring,
    IHandle<ProposeBehaviorChangeRequest>,
    IEmit<BehaviorChangeProposed>
{
    internal const string ModelNeuronName = "behavior-author";

    // Drafting is a handled request, so a behavior that never answers must not outlive one outbox
    // delivery attempt: past that the retry of the request being served starts while this handler is
    // still waiting, and the throw would be retried for the whole delivery horizon. The bound must
    // also come in strictly under DeliveryAttemptTimeout: TryDeliverAsync arms the outer attempt
    // deadline before this handler's turn starts, so a bound equal to it always loses that race and
    // this catch would never see a TimeoutException.
    internal static readonly TimeSpan BehaviorReadBound = DeliveryPolicy.InnerDeliveryReadBound;

    private const string StateName = "ai.behavior-authoring";
    private const string DefaultFeatureName = "install";
    internal const int RetainedEntries = 64;

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<AuthoringData> _states;

    public BehaviorAuthorNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<AuthoringData>>();
    }

    public Task<BehaviorChangeProposed> Propose(ProposeBehaviorChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProposeAsync(request, TurnCancellationToken);
    }

    public async Task HandleAsync(ProposeBehaviorChangeRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        await ReplyAsync(await ProposeAsync(synapse, cancellationToken), cancellationToken);
    }

    public async Task<BehaviorChangeDecision> Approve(ApproveBehaviorChange command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var data = LoadOrEmpty();
        if (data.Decisions.TryGet(command.CommandId.Value, out var settled))
        {
            return settled;
        }

        if (!data.Proposals.TryGet(command.ProposalId, out var proposal)
            || !string.Equals(proposal.BehaviorId, command.BehaviorId, StringComparison.Ordinal))
        {
            return BehaviorChangeDecision.Unknown;
        }

        if (!command.Approved)
        {
            return await SettleAsync(
                data,
                command,
                new BehaviorChangeDecision(proposal with { Status = BehaviorChangeStatus.Rejected }, Applied: false));
        }

        var behavior = Behavior(command.BehaviorId);
        var current = Authored.Of(command.BehaviorId, await behavior.Read());
        var applied = await Author().ApplyApprovedScenarios(
            new BehaviorChangeRequest(
                command.BehaviorId,
                proposal.RequestText,
                current.FeatureText,
                current.ProgramSource,
                current.DisplayName,
                proposal.ProposedFeatureName),
            new BehaviorScenarioProposal(
                proposal.ProposalId,
                string.IsNullOrWhiteSpace(command.FeatureText)
                    ? proposal.ProposedFeatureText
                    : command.FeatureText,
                proposal.DiffSummary ?? "approved scenario change"),
            TurnCancellationToken);

        var featureName = string.IsNullOrWhiteSpace(command.FeatureName)
            ? applied.FeatureName
            : command.FeatureName;

        // The approval's own command id carries into the revision so a repeated approval lands on
        // the behavior neuron's receipt instead of proposing the same source twice.
        await behavior.Propose(new ProposeBehaviorRevision(
            command.CommandId,
            applied.ProgramSource,
            new Dictionary<string, string>(StringComparer.Ordinal) { [featureName] = applied.FeatureText },
            current.DisplayName,
            current.Description));

        return await SettleAsync(data, command, new BehaviorChangeDecision(proposal, Applied: true));
    }

    private async Task<BehaviorChangeProposed> ProposeAsync(
        ProposeBehaviorChangeRequest request,
        CancellationToken cancellationToken)
    {
        var data = LoadOrEmpty();
        if (data.Drafts.TryGet(request.CommandId.Value, out var drafted))
        {
            return new BehaviorChangeProposed(request.CommandId, drafted);
        }

        BehaviorSnapshot snapshot;
        try
        {
            snapshot = await Behavior(request.BehaviorId).Read().WaitAsync(BehaviorReadBound, cancellationToken);
        }
        catch (TimeoutException)
        {
            return BehaviorChangeProposed.Refused(
                request.CommandId,
                $"Behavior '{request.BehaviorId}' did not answer within "
                + $"{BehaviorReadBound.TotalSeconds} seconds, so there is nothing to draft a change against.");
        }

        var current = Authored.Of(request.BehaviorId, snapshot);
        var authored = Author().ProposeScenarios(new BehaviorChangeRequest(
            request.BehaviorId,
            request.RequestText,
            current.FeatureText,
            current.ProgramSource,
            current.DisplayName,
            current.FeatureName));

        var proposal = new BehaviorChangeProposal(
            authored.ProposalId,
            request.BehaviorId,
            request.RequestText,
            authored.ProposedFeatureText,
            current.FeatureName,
            BehaviorChangeStatus.AwaitingScenarioApproval,
            authored.DiffSummary);

        await SaveAsync(data with
        {
            Drafts = data.Drafts.With(request.CommandId.Value, proposal, RetainedEntries),
            Proposals = data.Proposals.With(proposal.ProposalId, proposal, RetainedEntries),
        });
        return new BehaviorChangeProposed(request.CommandId, proposal);
    }

    private async Task<BehaviorChangeDecision> SettleAsync(
        AuthoringData data,
        ApproveBehaviorChange command,
        BehaviorChangeDecision decision)
    {
        await SaveAsync(data with
        {
            Proposals = data.Proposals.Without(command.ProposalId),
            Decisions = data.Decisions.With(command.CommandId.Value, decision, RetainedEntries),
        });
        return decision;
    }

    private IBehaviorNeuron Behavior(string behaviorId)
        => GrainFactory.GetGrain<IBehaviorNeuron>(
            NeuronId.For<IBehaviorNeuron>(Id.Owner, behaviorId).ToGrainId());

    // The model call rides the AI rail rather than an injected IChatClient, so the authoring turn is
    // journaled like any other model call and no non-LLM neuron takes a chat client.
    private IBehaviorAuthor Author()
        => ServiceProvider.GetService<IBehaviorAuthor>()
            ?? new BehaviorAuthor(async (messages, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await GrainFactory
                    .GetGrain<IGemma4>(NeuronId.For<IGemma4>(Id.Owner, ModelNeuronName).ToGrainId())
                    .Respond([.. messages]);
                return response.Text ?? string.Empty;
            });

    private AuthoringData LoadOrEmpty()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : AuthoringData.Empty;

    // Deliberately no EnlistTurnRollback here: ProposeAsync's idempotency (line 114's Drafts lookup
    // by CommandId) depends on a committed draft surviving a later turn retraction, or a retried
    // request would find the map empty again and draft a second, possibly different, proposal.
    private async Task SaveAsync(AuthoringData data)
    {
        var previous = _state.Value is { Length: > 0 } serialized ? serialized.ToArray() : [];
        _state.Value = _states.SerializeToArray(data);
        try
        {
            await WriteStateAsync();
        }
        catch
        {
            _state.Value = previous;
            throw;
        }
    }

    private sealed record Authored(
        string FeatureName,
        string FeatureText,
        string ProgramSource,
        string DisplayName,
        string Description)
    {
        public static Authored Of(string behaviorId, BehaviorSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            return new(
                string.IsNullOrWhiteSpace(snapshot.FeatureName) ? DefaultFeatureName : snapshot.FeatureName,
                string.IsNullOrWhiteSpace(snapshot.FeatureText) ? string.Empty : snapshot.FeatureText,
                string.IsNullOrWhiteSpace(snapshot.ProgramSource) ? string.Empty : snapshot.ProgramSource,
                string.IsNullOrWhiteSpace(snapshot.DisplayName) ? behaviorId : snapshot.DisplayName,
                string.IsNullOrWhiteSpace(snapshot.Description) ? behaviorId : snapshot.Description);
        }
    }

    // Every map is bounded, Proposals included: drafting is the model-callable half of the approval
    // boundary, so an unbounded proposal map would let a model grow durable owner state without
    // limit, each entry carrying whole feature texts. Past the bound the oldest unsettled draft is
    // dropped and has to be drafted again; a draft the owner never acted on is the cheapest thing to
    // lose, and the alternative is refusing to draft at all once an attacker has filled the map.
    [GenerateSerializer]
    internal sealed record AuthoringData
    {
        public static AuthoringData Empty { get; } = new();

        [Id(0)]
        public BoundedLedger<string, BehaviorChangeProposal> Proposals { get; init; } = new();

        [Id(1)]
        public BoundedLedger<Guid, BehaviorChangeProposal> Drafts { get; init; } = new();

        [Id(2)]
        public BoundedLedger<Guid, BehaviorChangeDecision> Decisions { get; init; } = new();
    }
}
