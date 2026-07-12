using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Core.Runtime;

public enum EffectDisposition { Success, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled }
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed record EffectExecutionResult(EffectDisposition Disposition, string? SafeResult = null, string? ProviderOperationId = null);
public sealed record EffectVerificationResult(bool Verified, bool NotFound, string? SafeReason = null);

public interface IEffectHandler
{
    string EffectType { get; }

    /// <summary>The stable <see cref="OutboxRecord.OperationId"/> is the provider idempotency key for every attempt.</summary>
    Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default);
}

public interface IEffectVerifier
{
    string EffectType { get; }
    Task<EffectVerificationResult> VerifyAsync(OutboxRecord intent, CancellationToken cancellationToken = default);
}

public sealed class EffectCoordinator
{
    private readonly IAggregateStore _store;
    private readonly Dictionary<string, IEffectHandler> _handlers;
    private readonly Dictionary<string, IEffectVerifier> _verifiers;
    private readonly IClock _clock;

    public EffectCoordinator(
        IAggregateStore store,
        IEnumerable<IEffectHandler> handlers,
        IClock? clock = null,
        IEnumerable<IEffectVerifier>? verifiers = null)
    {
        _store = store;
        var handlerList = handlers.ToArray();
        _handlers = handlerList.ToDictionary(static handler => handler.EffectType, StringComparer.Ordinal);
        _verifiers = (verifiers ?? [])
            .Concat(handlerList.OfType<IEffectVerifier>())
            .GroupBy(static verifier => verifier.EffectType, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        _clock = clock ?? new SystemClock();
    }

    public async Task<EffectTransitionRecord> ExecuteOnceAsync(
        string aggregateId,
        string effectId,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseOwner);
        if (leaseOwner.Length > 1024) throw new ArgumentOutOfRangeException(nameof(leaseOwner));
        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));

        while (true)
        {
            var snapshot = await _store.ReadAsync(aggregateId, cancellationToken).ConfigureAwait(false);
            var latest = snapshot.EffectTransitions.LastOrDefault(transition => transition.EffectId == effectId);
            if (latest is not null && AggregateRetention.IsTerminalEffectState(latest.State)) return latest;

            var intent = snapshot.Outbox.FirstOrDefault(item => item.EffectId == effectId)
                         ?? throw new KeyNotFoundException("The durable effect was not found.");
            if (latest is { State: "Applying" })
            {
                if (latest.LeaseExpiresAt is { } leaseExpiry && leaseExpiry > _clock.UtcNow)
                    throw new InvalidOperationException("The durable effect lease is held.");

                var verification = await VerifyUncertainAsync(intent, cancellationToken).ConfigureAwait(false);
                if (verification.Verified)
                {
                    var succeeded = await TryResolveAsync(
                        aggregateId,
                        intent,
                        latest,
                        "Succeeded",
                        verification.SafeReason,
                        cancellationToken).ConfigureAwait(false);
                    if (succeeded is not null) return succeeded;
                    continue;
                }
                if (!verification.NotFound)
                {
                    var unknown = await TryResolveAsync(
                        aggregateId,
                        intent,
                        latest,
                        "OutcomeUnknown",
                        verification.SafeReason ?? "effect-outcome-unverifiable",
                        cancellationToken).ConfigureAwait(false);
                    if (unknown is not null) return unknown;
                    continue;
                }
            }
            else if (latest is not null && latest.State != "RetryScheduled")
            {
                var unknown = await TryResolveAsync(
                    aggregateId,
                    intent,
                    latest,
                    "OutcomeUnknown",
                    "effect-state-unrecognized",
                    cancellationToken).ConfigureAwait(false);
                if (unknown is not null) return unknown;
                continue;
            }

            var applying = NewTransition(
                effectId,
                "Applying",
                null,
                LeaseOwnerToken(leaseOwner),
                _clock.UtcNow.Add(leaseDuration));
            if (!await _store.TryAppendEffectTransitionAsync(
                    aggregateId,
                    effectId,
                    latest?.TransitionId,
                    applying,
                    cancellationToken).ConfigureAwait(false))
                continue;

            return await ExecuteClaimedAsync(aggregateId, intent, applying, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<EffectVerificationResult> VerifyUncertainAsync(OutboxRecord intent, CancellationToken cancellationToken)
    {
        if (!_verifiers.TryGetValue(intent.EffectType, out var verifier))
            return new(false, false, "effect-outcome-unverifiable");
        try
        {
            return await verifier.VerifyAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(false, false, "effect-verification-failed");
        }
    }

    private async Task<EffectTransitionRecord> ExecuteClaimedAsync(
        string aggregateId,
        OutboxRecord intent,
        EffectTransitionRecord applying,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(intent.EffectType, out var handler))
            return await CompleteClaimAsync(aggregateId, intent, applying, "Failed", "effect-handler-unavailable", null, cancellationToken).ConfigureAwait(false);
        if (intent.Deadline <= _clock.UtcNow)
            return await CompleteClaimAsync(aggregateId, intent, applying, "Failed", "deadline-exceeded", null, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
            return await CompleteClaimAsync(aggregateId, intent, applying, "Cancelled", "cancelled-before-dispatch", null, CancellationToken.None).ConfigureAwait(false);

        EffectExecutionResult result;
        try
        {
            result = await handler.ExecuteAsync(intent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await CompleteClaimAsync(aggregateId, intent, applying, "OutcomeUnknown", "effect-execution-cancelled", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            result = new(EffectDisposition.OutcomeUnknown, "effect-execution-failed");
        }

        var state = result.Disposition switch
        {
            EffectDisposition.Success => "Succeeded",
            EffectDisposition.RetryableFailure when intent.Deadline > _clock.UtcNow => "RetryScheduled",
            EffectDisposition.OutcomeUnknown => "OutcomeUnknown",
            EffectDisposition.Cancelled => "OutcomeUnknown",
            _ => "Failed"
        };
        var safeResult = result.Disposition == EffectDisposition.Cancelled
            ? "effect-execution-cancelled"
            : result.SafeResult;
        return await CompleteClaimAsync(
            aggregateId,
            intent,
            applying,
            state,
            safeResult,
            result.ProviderOperationId,
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<EffectTransitionRecord?> TryResolveAsync(
        string aggregateId,
        OutboxRecord intent,
        EffectTransitionRecord expected,
        string state,
        string? safeResult,
        CancellationToken cancellationToken)
    {
        var transition = NewTransition(intent.EffectId, state, safeResult);
        return await _store.TryAppendEffectTransitionAsync(
            aggregateId,
            intent.EffectId,
            expected.TransitionId,
            transition,
            cancellationToken).ConfigureAwait(false)
            ? transition
            : null;
    }

    private async Task<EffectTransitionRecord> CompleteClaimAsync(
        string aggregateId,
        OutboxRecord intent,
        EffectTransitionRecord applying,
        string state,
        string? safeResult,
        string? providerOperationId,
        CancellationToken cancellationToken)
    {
        var transition = NewTransition(intent.EffectId, state, safeResult, providerOperationId: providerOperationId);
        if (await _store.TryAppendEffectTransitionAsync(
                aggregateId,
                intent.EffectId,
                applying.TransitionId,
                transition,
                cancellationToken).ConfigureAwait(false))
            return transition;

        var latest = (await _store.ReadAsync(aggregateId, cancellationToken).ConfigureAwait(false))
            .EffectTransitions.LastOrDefault(item => item.EffectId == intent.EffectId);
        if (latest is not null && AggregateRetention.IsTerminalEffectState(latest.State)) return latest;
        throw new InvalidOperationException("The durable effect state changed before its outcome could be recorded.");
    }

    private EffectTransitionRecord NewTransition(
        string effectId,
        string state,
        string? safeResult,
        string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null,
        string? providerOperationId = null) => new(
        effectId,
        "v2-transition-" + Guid.NewGuid().ToString("N"),
        state,
        Redaction.SafeSummary(safeResult),
        _clock.UtcNow,
        leaseOwner,
        leaseExpiresAt,
        Redaction.SafeSummary(providerOperationId));

    private static string LeaseOwnerToken(string leaseOwner) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(leaseOwner)));
}
