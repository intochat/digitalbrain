using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Client;

namespace DigitalBrain.UI;

internal static class BehaviorEndpoints
{
    public static IEndpointRouteBuilder MapBehaviors(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            UiHttpContract.BehaviorPath,
            static async Task<IResult> (
                string behaviorId,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                var snapshot = await brain.Get<IBehaviorNeuron>(behaviorId).Read();
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            UiHttpContract.BehaviorProposePath,
            static async Task<IResult> (
                string behaviorId,
                ProposeBehaviorRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ProgramSource)
                    || string.IsNullOrWhiteSpace(request.FeatureText))
                {
                    return Results.BadRequest();
                }

                var featureName = string.IsNullOrWhiteSpace(request.FeatureName)
                    ? AccountEnrichmentEditorSeed.FeatureName
                    : request.FeatureName.Trim();
                var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? behaviorId
                    : request.DisplayName.Trim();
                var description = string.IsNullOrWhiteSpace(request.Description)
                    ? behaviorId
                    : request.Description.Trim();

                var snapshot = await brain.Get<IBehaviorNeuron>(behaviorId).Propose(new ProposeBehaviorRevision(
                    CommandId.New(),
                    request.ProgramSource,
                    new Dictionary<string, string>(StringComparer.Ordinal) { [featureName] = request.FeatureText },
                    displayName,
                    description));

                return Results.Ok(ToDocument(
                    behaviorId,
                    snapshot,
                    request.ProgramSource,
                    featureName,
                    request.FeatureText,
                    displayName,
                    description));
            });

        endpoints.MapPost(
            UiHttpContract.BehaviorTestsPath,
            static async Task<IResult> (
                string behaviorId,
                RunBehaviorTestsRequest request,
                IDigitalBrain brain,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ArtifactHash))
                {
                    return Results.BadRequest();
                }

                var snapshot = await brain.Get<IBehaviorNeuron>(behaviorId).RunTests(
                    new RunBehaviorTests(CommandId.New(), request.ArtifactHash));
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        endpoints.MapPost(
            UiHttpContract.BehaviorApprovePath,
            static async Task<IResult> (
                string behaviorId,
                ApproveBehaviorRequest request,
                IDigitalBrain brain,
                IGrainFactory grains,
                CancellationToken cancellationToken) =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(brain);
                ArgumentNullException.ThrowIfNull(grains);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.ArtifactHash)
                    || !Guid.TryParse(request.ApprovalId, out var approvalIdentity)
                    || approvalIdentity == Guid.Empty)
                {
                    return Results.BadRequest();
                }

                var approval = new BehaviorRevisionApproval(
                    approvalIdentity,
                    CommandId.New(),
                    request.ArtifactHash,
                    ISessionNeuron.ForOwner(brain.Owner),
                    DateTimeOffset.UtcNow);

                var neuron = brain.Get<IBehaviorNeuron>(behaviorId);
                await brain.SendAsync(NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId), approval);

                var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(brain.Owner).ToGrainId());
                var neuronId = NeuronId.For<IBehaviorNeuron>(brain.Owner, behaviorId);
                var after = 0L;
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var journal = await session.ReadNeuronJournal(neuronId, JournalKind.Incoming, after);
                    if (journal.Delta.Any(delivery =>
                            delivery.Synapse is BehaviorRevisionApproval recorded
                            && recorded == approval
                            && delivery.Caller == approval.Approver))
                    {
                        break;
                    }

                    after = journal.ResumeSequence;
                    await Task.Delay(20, cancellationToken);
                }

                var snapshot = await neuron.Approve(approval);
                return Results.Ok(ToDocument(behaviorId, snapshot));
            });

        return endpoints;
    }

    private static BehaviorEditorDocument ToDocument(
        string behaviorId,
        BehaviorSnapshot snapshot,
        string? programSource = null,
        string? featureName = null,
        string? featureText = null,
        string? displayName = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentNullException.ThrowIfNull(snapshot);

        var seeded = string.Equals(
            behaviorId,
            UiHttpContract.AccountEnrichmentBehaviorId,
            StringComparison.Ordinal);

        return new BehaviorEditorDocument(
            behaviorId,
            snapshot.Status.ToString(),
            snapshot.ProposedArtifactHash,
            snapshot.ActiveArtifactHash,
            snapshot.PriorArtifactHash,
            snapshot.LastCompileFailure,
            snapshot.TestsPassed,
            snapshot.IsApproved,
            snapshot.LastExecutionOutcome,
            programSource
                ?? (seeded ? AccountEnrichmentEditorSeed.ProgramSource : string.Empty),
            featureName
                ?? (seeded ? AccountEnrichmentEditorSeed.FeatureName : "install"),
            featureText
                ?? (seeded ? AccountEnrichmentEditorSeed.FeatureText : string.Empty),
            displayName
                ?? (seeded ? AccountEnrichmentEditorSeed.DisplayName : behaviorId),
            description
                ?? (seeded ? AccountEnrichmentEditorSeed.Description : behaviorId));
    }
}
