using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureRunProjection
{
    private const string FailedGuidance = "DigitalBrain will retry automatically. Review the Feature and its Connections if the failure continues.";
    private const string DeclinedFailure = "The proposed external action was declined. No external action was performed.";
    private const string DeclinedGuidance = "Review the Feature or run it again if you want to propose a different action.";
    private const string PausedFailureGuidance = "Resume the Feature when it is safe to retry, or review its Connections first.";
    private const string ParkedFailure = "This Run needs attention before it can continue.";
    private const string ParkedGuidance = "Review the Feature and its Connections before retrying this Run.";

    public static FeatureRunSnapshot[] Project(FeatureInstallationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var runs = new List<FeatureRunSnapshot>(state.Inbox.Length + state.Completions.Length);
        foreach (var entry in state.Inbox)
        {
            if (entry.AcceptedRelease is not { } release)
                continue;
            var identity = Identity(entry.Input);
            var runId = ProjectedRunId(state.InstallationId, identity.RunId);
            var running = state.Lease is { } lease &&
                string.Equals(lease.Fence.InputId, identity.RunId, StringComparison.Ordinal);
            var status = running
                ? FeatureRunStatus.Running
                : entry.Parked
                    ? FeatureRunStatus.Parked
                    : entry.LastFailure is not null
                        ? FeatureRunStatus.Failed
                        : FeatureRunStatus.Queued;
            runs.Add(new FeatureRunSnapshot(
                runId,
                state.InstallationId,
                release,
                identity.InputKind,
                identity.Origin,
                identity.OriginReference,
                status,
                entry.Parked || state.Paused
                    ? FeatureRunAuthorityState.Paused
                    : FeatureRunAuthorityState.Authorized,
                identity.OccurredAt,
                entry.LastAttemptAt ?? (entry.Attempts > 0 ? identity.OccurredAt : null),
                null,
                status == FeatureRunStatus.Failed ? entry.NotBefore : null,
                Math.Max(entry.Attempts, entry.TotalAttempts),
                null,
                status switch
                {
                    FeatureRunStatus.Failed => entry.LastFailure,
                    FeatureRunStatus.Parked => entry.LastFailure ?? ParkedFailure,
                    _ => null
                },
                status switch
                {
                    FeatureRunStatus.Failed when state.Paused => PausedFailureGuidance,
                    FeatureRunStatus.Failed => FailedGuidance,
                    FeatureRunStatus.Parked => ParkedGuidance,
                    _ => null
                },
                identity.TraceReference));
        }
        foreach (var completion in state.Completions)
        {
            if (completion.Release is not { } release || completion.Run is not { } identity)
                continue;
            var runId = ProjectedRunId(state.InstallationId, identity.RunId);
            var intents = state.Intents.Where(intent =>
                string.Equals(intent.InputId, identity.RunId, StringComparison.Ordinal)).ToArray();
            var legacyEffects = intents.Where(intent => intent.Kind == FeatureIntentKind.ExternalEffect).ToArray();
            var resolutions = (completion.EffectResolutions ?? [])
                .Select(Effect)
                .Concat(legacyEffects.Select(Effect).Where(effect => effect is not null).Cast<EffectProjection>())
                .GroupBy(effect => effect.OperationKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var waiting = completion.EffectCount > 0
                ? resolutions.Select(effect => effect.OperationKey).Distinct(StringComparer.Ordinal).Count() < completion.EffectCount
                : legacyEffects.Any(intent => Effect(intent) is null);
            var failed = resolutions.Any(effect => effect.TerminalKind is
                InoEffectTerminalKind.Declined or InoEffectTerminalKind.Expired or InoEffectTerminalKind.Failed);
            var outcomeUnknown = resolutions.Any(effect => effect.TerminalKind == InoEffectTerminalKind.OutcomeUnknown);
            var status = waiting
                ? FeatureRunStatus.WaitingForApproval
                : failed
                    ? FeatureRunStatus.Failed
                    : outcomeUnknown
                        ? FeatureRunStatus.Parked
                        : FeatureRunStatus.Completed;
            var terminalAt = resolutions
                .Select(effect => effect.ResolvedAt)
                .DefaultIfEmpty(completion.CompletedAt)
                .Append(completion.CompletedAt)
                .Max();
            var adverse = resolutions
                .Where(effect => status switch
                {
                    FeatureRunStatus.Failed => effect.TerminalKind is
                        InoEffectTerminalKind.Declined or InoEffectTerminalKind.Expired or InoEffectTerminalKind.Failed,
                    FeatureRunStatus.Parked => effect.TerminalKind == InoEffectTerminalKind.OutcomeUnknown,
                    _ => false
                })
                .OrderByDescending(effect => effect.ResolvedAt)
                .ThenBy(effect => effect.OperationKey, StringComparer.Ordinal)
                .FirstOrDefault();
            runs.Add(new FeatureRunSnapshot(
                runId,
                state.InstallationId,
                release,
                identity.InputKind,
                identity.Origin,
                identity.OriginReference,
                status,
                waiting ? FeatureRunAuthorityState.WaitingForApproval : FeatureRunAuthorityState.Authorized,
                identity.OccurredAt,
                completion.StartedAt,
                terminalAt,
                null,
                completion.Attempts,
                status == FeatureRunStatus.Completed && completion.HasResultSurface ? ResultSurfaceReference(runId) : null,
                adverse?.SafeResult,
                adverse?.TerminalKind == InoEffectTerminalKind.Declined
                    ? DeclinedGuidance
                    : adverse is null
                        ? null
                        : ParkedGuidance,
                identity.TraceReference));
        }
        return runs.ToArray();
    }

    public static FeatureRunIdentity Identity(FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var origin = EffectiveOrigin(input);
        var reference = EffectiveReference(input, origin);
        ValidateReference(reference, origin);
        return new FeatureRunIdentity(
            input.InputId,
            input.Kind,
            origin,
            reference,
            input.OccurredAt,
            OpaqueReference("trace", input.TraceId));
    }

    public static void ValidateOrigin(FeatureInput input)
    {
        _ = Identity(input);
    }

    public static void AppendOriginDigest(StringBuilder canonical, FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var identity = Identity(input);
        canonical.Append((int)identity.Origin).Append(';');
        AppendCanonical(canonical, identity.OriginReference?.ConversationId ?? string.Empty);
        AppendCanonical(canonical, identity.OriginReference?.RequestId ?? string.Empty);
        AppendCanonical(canonical, identity.OriginReference?.AutomationId ?? string.Empty);
    }

    private static FeatureRunOrigin EffectiveOrigin(FeatureInput input)
    {
        if (input.Origin != FeatureRunOrigin.Unspecified)
            return input.Origin;
        if (input.Kind.StartsWith("schedule.", StringComparison.Ordinal))
            return FeatureRunOrigin.Schedule;
        return string.Equals(input.Kind, "manual", StringComparison.Ordinal)
            ? FeatureRunOrigin.Direct
            : FeatureRunOrigin.Event;
    }

    private static FeatureRunOriginReference? EffectiveReference(FeatureInput input, FeatureRunOrigin origin)
    {
        if (input.OriginReference is not null)
            return input.OriginReference;
        return origin switch
        {
            FeatureRunOrigin.Schedule or FeatureRunOrigin.Event => new FeatureRunOriginReference(null, null, input.Kind),
            _ => null
        };
    }

    private static void ValidateReference(FeatureRunOriginReference? reference, FeatureRunOrigin origin)
    {
        if (origin == FeatureRunOrigin.Unspecified)
            throw new ArgumentException("A Feature Run origin is required.");
        if (reference is not null)
        {
            ValidateOptional(reference.ConversationId);
            ValidateOptional(reference.RequestId);
            ValidateOptional(reference.AutomationId);
        }
        var valid = origin switch
        {
            FeatureRunOrigin.Chat => reference is
            {
                ConversationId: not null,
                RequestId: not null,
                AutomationId: null
            },
            FeatureRunOrigin.Direct => reference is null || reference is
            {
                ConversationId: null,
                RequestId: null,
                AutomationId: null
            },
            FeatureRunOrigin.Schedule or FeatureRunOrigin.Event => reference is
            {
                ConversationId: null,
                RequestId: null,
                AutomationId: not null
            },
            _ => false
        };
        if (!valid)
            throw new ArgumentException("The Feature Run origin reference is invalid.");
    }

    private static void ValidateOptional(string? value)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("A bounded canonical Run origin reference is required.");
    }

    private static string ProjectedRunId(FeatureInstallationId installationId, string inputId) =>
        OpaqueReference("run", $"{installationId.Value.Length}:{installationId.Value}{inputId.Length}:{inputId}");

    private static string ResultSurfaceReference(string runId) =>
        OpaqueReference("result", runId);

    private static string OpaqueReference(string kind, string value) =>
        $"{kind}-{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    private static void AppendCanonical(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value).Append(';');

    private static EffectProjection Effect(FeatureEffectResolution resolution) => new(
        resolution.OperationKey,
        resolution.TerminalKind,
        resolution.ResolvedAt,
        resolution.SafeResult);

    private static EffectProjection? Effect(PersistedFeatureIntent intent)
    {
        if (intent.Resolution is { } resolution)
            return Effect(resolution);
        if (intent.AppliedAt is { } appliedAt)
            return new(intent.OperationKey, InoEffectTerminalKind.Approved, appliedAt, "The action completed.");
        if (intent.DeclinedAt is { } declinedAt)
            return new(intent.OperationKey, InoEffectTerminalKind.Declined, declinedAt, DeclinedFailure);
        return null;
    }

    private sealed record EffectProjection(
        string OperationKey,
        InoEffectTerminalKind TerminalKind,
        DateTimeOffset ResolvedAt,
        string SafeResult);
}
