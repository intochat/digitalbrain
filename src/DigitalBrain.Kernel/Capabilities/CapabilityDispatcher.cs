using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Kernel.Capabilities;

internal sealed class CapabilityDispatcher : ICapabilityDispatcher
{
    private readonly IReadOnlyDictionary<string, ICapabilityHandler> _handlers;
    private readonly ICapabilityGrantSource _grants;
    private readonly TimeProvider _timeProvider;
    private readonly CapabilityGrantValidator _validator;
    public CapabilityDispatcher(IEnumerable<ICapabilityHandler> handlers, ICapabilityGrantSource grants, TimeProvider timeProvider, CapabilityGrantValidator? validator = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _validator = validator ?? new CapabilityGrantValidator();
        var registered = new Dictionary<string, ICapabilityHandler>(StringComparer.Ordinal);
        foreach (var handler in handlers)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.CapabilityId);
            ArgumentOutOfRangeException.ThrowIfLessThan(handler.CapabilityVersion, 1);
            if (!registered.TryAdd(handler.CapabilityId, handler))
                throw new InvalidOperationException($"Capability handler '{handler.CapabilityId}' is registered more than once.");
        }
        _handlers = registered;
    }
    public async Task<CapabilityDispatchResult> ExecuteAsync(CapabilityRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_handlers.TryGetValue(request.CapabilityId, out var handler) || handler.CapabilityVersion != request.CapabilityVersion)
            throw new CapabilityDeniedException();
        var grant = await _grants.ReadAsync(request, cancellationToken).ConfigureAwait(false);
        var remaining = _validator.Validate(request, grant, _timeProvider.GetUtcNow());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(remaining);
        try
        {
            var execution = handler.ExecuteAsync(request, grant!, deadline.Token);
            var payload = await execution.WaitAsync(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
            return new CapabilityDispatchResult(handler.OperationKind, payload);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Capability execution exceeded its deadline.");
        }
    }
}
