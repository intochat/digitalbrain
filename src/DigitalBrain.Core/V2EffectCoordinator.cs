namespace DigitalBrain.Core.V2;

public enum V2EffectDisposition { Success, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled }
public interface IV2Clock { DateTimeOffset UtcNow { get; } }
public sealed class SystemV2Clock : IV2Clock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed record V2EffectExecutionResult(V2EffectDisposition Disposition, string? SafeResult = null, string? ProviderOperationId = null);
public sealed record V2EffectVerificationResult(bool Verified, bool NotFound, string? SafeReason = null);

public interface IV2EffectHandler
{
    string EffectType { get; }
    Task<V2EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default);
}

public interface IV2EffectVerifier
{
    Task<V2EffectVerificationResult> VerifyAsync(OutboxRecord intent, CancellationToken cancellationToken = default);
}

public sealed class V2EffectCoordinator(IV2AggregateStore store, IEnumerable<IV2EffectHandler> handlers, IV2Clock? clock = null)
{
    private readonly Dictionary<string, IV2EffectHandler> _handlers = handlers.ToDictionary(x => x.EffectType, StringComparer.Ordinal);
    private readonly IV2Clock _clock = clock ?? new SystemV2Clock();
    private readonly Dictionary<string, DateTimeOffset> _leases = new(StringComparer.Ordinal);

    public async Task<EffectTransitionRecord> ExecuteOnceAsync(string aggregateId, string effectId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var snapshot = await store.ReadAsync(aggregateId, cancellationToken);
        var intent = snapshot.Outbox.FirstOrDefault(x => x.EffectId == effectId) ?? throw new KeyNotFoundException("V2 effect was not found.");
        lock (_leases)
        {
            if (_leases.TryGetValue(effectId, out var expiry) && expiry > _clock.UtcNow) throw new InvalidOperationException("V2 effect lease is held.");
            _leases[effectId] = _clock.UtcNow.Add(leaseDuration);
        }
        try
        {
            await AddTransition(aggregateId, effectId, "Applying", null, cancellationToken);
            if (!_handlers.TryGetValue(intent.EffectType, out var handler)) return await AddTransition(aggregateId, effectId, "Failed", "effect-handler-unavailable", cancellationToken);
            if (intent.Deadline <= _clock.UtcNow) return await AddTransition(aggregateId, effectId, "Failed", "deadline-exceeded", cancellationToken);
            V2EffectExecutionResult result;
            try { result = await handler.ExecuteAsync(intent, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return await AddTransition(aggregateId, effectId, "Cancelled", "cancelled", cancellationToken); }
            catch (Exception ex) { result = new V2EffectExecutionResult(V2EffectDisposition.OutcomeUnknown, V2Redaction.SafeSummary(ex.Message)); }
            return result.Disposition switch
            {
                V2EffectDisposition.Success => await AddTransition(aggregateId, effectId, "Succeeded", result.SafeResult, cancellationToken),
                V2EffectDisposition.RetryableFailure when intent.Deadline > _clock.UtcNow => await AddTransition(aggregateId, effectId, "RetryScheduled", result.SafeResult, cancellationToken),
                V2EffectDisposition.OutcomeUnknown => await AddTransition(aggregateId, effectId, "OutcomeUnknown", result.SafeResult, cancellationToken),
                V2EffectDisposition.Cancelled => await AddTransition(aggregateId, effectId, "Cancelled", result.SafeResult, cancellationToken),
                _ => await AddTransition(aggregateId, effectId, "Failed", result.SafeResult, cancellationToken),
            };
        }
        finally { lock (_leases) _leases.Remove(effectId); }
    }

    private async Task<EffectTransitionRecord> AddTransition(string aggregateId, string effectId, string state, string? safeResult, CancellationToken cancellationToken)
    {
        var transition = new EffectTransitionRecord(effectId, "v2-transition-" + Guid.NewGuid().ToString("N"), state, V2Redaction.SafeSummary(safeResult), _clock.UtcNow);
        await store.AppendEffectTransitionAsync(aggregateId, transition, cancellationToken);
        return transition;
    }
}
