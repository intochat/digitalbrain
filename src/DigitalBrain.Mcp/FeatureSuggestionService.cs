using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Orleans;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed class FeatureSuggestionService(IClusterClient cluster)
{
    public async Task<FeatureDraftPatch> SuggestAsync(
        RuntimeRequestContext context,
        SuggestFeatureChange command,
        CancellationToken cancellationToken = default)
    {
        DemandFeatureAuthor(context);
        ArgumentNullException.ThrowIfNull(command);
        var key = FeatureGrainIds.Hub(context.OwnerId);
        var draft = await cluster.GetGrain<IFeatureHubGrain>(key)
            .ReadDraftAsync(command.DraftId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Feature Draft was not found.");
        if (draft.Revision != command.ExpectedRevision)
            throw new InvalidOperationException("The Feature Draft revision is stale.");
        return await cluster.GetGrain<IFeatureSuggestionModelGrain>(key)
            .SuggestAsync(command, cancellationToken)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void DemandFeatureAuthor(RuntimeRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.OwnerId.Value) ||
            string.IsNullOrWhiteSpace(context.ActorId.Value) ||
            string.IsNullOrWhiteSpace(context.SessionId.Value) ||
            context.Assurance == AuthAssurance.None ||
            !Enum.IsDefined(context.Assurance))
            throw new UnauthorizedAccessException("An authenticated owner-scoped actor is required.");
        if (context.Grants is null || !context.Grants.Any(grant =>
                string.Equals(grant, "feature.manage", StringComparison.Ordinal)))
            throw new UnauthorizedAccessException("The authenticated principal lacks Feature management authority.");
    }
}
