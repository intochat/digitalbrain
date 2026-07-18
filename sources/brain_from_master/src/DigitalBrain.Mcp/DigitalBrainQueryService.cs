using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;

namespace DigitalBrain.Mcp;

public sealed record DigitalBrainRun(
    FeatureDraftId FeatureId,
    string FeatureGoal,
    FeatureRunSnapshot Run);

public sealed class DigitalBrainQueryService(IFeatureLifecycleRail lifecycle)
{
    public const int DefaultListLimit = 50;
    public const int MaximumListLimit = FeatureRunReadRequest.MaximumLimit;

    public async Task<IReadOnlyList<DigitalBrainRun>> ListRunsAsync(
        RuntimeRequestContext context,
        FeatureRunStatus? status = null,
        FeatureRunOrigin? origin = null,
        FeatureDraftId? featureId = null,
        int limit = DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        DemandAuthenticated(context);
        DemandFilter(status);
        DemandFilter(origin);
        if (featureId is not null)
            DemandIdentifier(featureId.Value, nameof(featureId));
        if (limit is < 1 or > MaximumListLimit)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var runs = await ProjectAsync(
            context,
            new FeatureRunReadRequest(limit, status, origin),
            cancellationToken).ConfigureAwait(false);
        return runs
            .Where(candidate => status is null || candidate.Run.Status == status)
            .Where(candidate => origin is null || candidate.Run.Origin == origin)
            .Where(candidate => featureId is null || candidate.FeatureId == featureId)
            .OrderByDescending(candidate => candidate.Run.CompletedAt ?? candidate.Run.OccurredAt)
            .ThenByDescending(candidate => candidate.Run.OccurredAt)
            .ThenBy(candidate => candidate.Run.RunId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<DigitalBrainRun> GetRunAsync(
        RuntimeRequestContext context,
        string runId,
        CancellationToken cancellationToken = default)
    {
        DemandAuthenticated(context);
        DemandIdentifier(runId, nameof(runId));
        var runs = await ProjectAsync(
            context,
            new FeatureRunReadRequest(1, RunId: runId),
            cancellationToken).ConfigureAwait(false);
        return runs.FirstOrDefault(candidate =>
                   string.Equals(candidate.Run.RunId, runId, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException("The Feature Run was not found.");
    }

    private async Task<DigitalBrainRun[]> ProjectAsync(
        RuntimeRequestContext context,
        FeatureRunReadRequest request,
        CancellationToken cancellationToken)
    {
        var inspectionSnapshot = await lifecycle.InspectRunsAsync(context, request, cancellationToken).ConfigureAwait(false);
        var projected = new List<DigitalBrainRun>();
        var installationCoordinates = new HashSet<FeatureInstallationId>();
        var featureCoordinates = new Dictionary<string, FeatureInstallationId>(StringComparer.Ordinal);
        var runIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var inspection in inspectionSnapshot.Installations)
        {
            ArgumentNullException.ThrowIfNull(inspection);
            if (inspection.Authority.ActorId != context.ActorId)
                continue;
            if (inspection.Runtime?.Runs is not { } runs)
                continue;
            if (!installationCoordinates.Add(inspection.Authority.InstallationId))
                throw new InvalidDataException("The Feature installation coordinate is ambiguous.");

            var draft = DemandCoordinate(inspection);
            if (featureCoordinates.TryGetValue(draft.DraftId.Value, out var existingInstallationId) &&
                existingInstallationId != inspection.Authority.InstallationId)
                throw new InvalidDataException("The Feature coordinate is ambiguous.");
            featureCoordinates[draft.DraftId.Value] = inspection.Authority.InstallationId;

            foreach (var run in runs)
            {
                ArgumentNullException.ThrowIfNull(run);
                DemandRun(run, inspection.Authority.InstallationId);
                if (!runIds.Add(run.RunId))
                    throw new InvalidDataException("The Feature Run identifier is ambiguous.");
                projected.Add(new DigitalBrainRun(draft.DraftId, draft.Goal, run));
            }
        }

        return projected.ToArray();
    }

    private static FeatureDraft DemandCoordinate(FeatureRunInstallationInspection inspection)
    {
        var authority = inspection.Authority;
        var registration = inspection.Registration
            ?? throw new InvalidDataException("The Feature registration coordinate is missing.");
        var runtime = inspection.Runtime
            ?? throw new InvalidDataException("The Feature runtime coordinate is missing.");
        var draft = inspection.Draft
            ?? throw new InvalidDataException("The installed Feature Draft coordinate is missing.");
        var activeRelease = authority.ActiveRelease
            ?? throw new InvalidDataException("The Feature authority has no active release.");

        if (registration.InstallationId != authority.InstallationId ||
            runtime.InstallationId != authority.InstallationId ||
            draft.InstallationId != authority.InstallationId ||
            registration.Release != activeRelease ||
            runtime.ActiveRelease != activeRelease ||
            draft.Verification?.Release != activeRelease)
            throw new InvalidDataException("The Feature installation coordinate is inconsistent.");

        DemandIdentifier(draft.DraftId.Value, nameof(draft.DraftId));
        if (string.IsNullOrWhiteSpace(draft.Goal) || draft.Goal.Length > 4096 || draft.Goal.Any(char.IsControl))
            throw new InvalidDataException("The installed Feature goal is invalid.");
        return draft;
    }

    private static void DemandRun(FeatureRunSnapshot run, FeatureInstallationId installationId)
    {
        try
        {
            DemandIdentifier(run.RunId, nameof(run.RunId));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The Feature Run identifier is invalid.", exception);
        }
        if (run.InstallationId != installationId ||
            run.Origin == FeatureRunOrigin.Unspecified || !Enum.IsDefined(run.Origin) ||
            !Enum.IsDefined(run.Status) || !Enum.IsDefined(run.AuthorityState))
            throw new InvalidDataException("The Feature Run coordinate is inconsistent.");
    }

    private static void DemandAuthenticated(RuntimeRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Enum.IsDefined(context.Assurance) || context.Assurance == AuthAssurance.None ||
            string.IsNullOrEmpty(context.OwnerId.Value) ||
            string.IsNullOrEmpty(context.ActorId.Value))
            throw new UnauthorizedAccessException("An authenticated owner-scoped actor is required.");
    }

    private static void DemandFilter(FeatureRunStatus? status)
    {
        if (status is not null && !Enum.IsDefined(status.Value))
            throw new ArgumentOutOfRangeException(nameof(status));
    }

    private static void DemandFilter(FeatureRunOrigin? origin)
    {
        if (origin is not null && (!Enum.IsDefined(origin.Value) || origin == FeatureRunOrigin.Unspecified))
            throw new ArgumentOutOfRangeException(nameof(origin));
    }

    private static void DemandIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical identifier is required.", parameterName);
    }
}
