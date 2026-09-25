using System.Diagnostics;
using DigitalBrain.Contracts;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class NeuronCallObservation(ActivityFeed feed) : IIncomingGrainCallFilter, IOutgoingGrainCallFilter
{
    internal const string ScopeKey = "digitalbrain.activity.scope";
    private const string OperationKey = "digitalbrain.activity.operation";
    private const string SourceKey = "digitalbrain.activity.source";
    private const string CorrelationKey = "digitalbrain.activity.correlation";

    private static bool IsObserved(Type type, string method) =>
        typeof(INeuron).IsAssignableFrom(type) && method is not nameof(INeuron.Watch) and not nameof(INeuron.Unwatch);

    private static string? Scope()
        => IntentContext.Current?.ScopeId ?? RequestContext.Get(ScopeKey) as string;

    private static string? Correlation()
        => IntentContext.Current?.IntentId ?? RequestContext.Get(CorrelationKey) as string;

    private static Guid Operation()
        => RequestContext.Get(OperationKey) is Guid id ? id : Guid.NewGuid();

    private void Record(string scope, Guid operation, ActivityKind kind, string? source, string? target,
        string type, string status, double? duration = null, string? failure = null)
        => feed.Append(new ActivityEvent(scope, 0, Guid.NewGuid(), operation, Correlation(), DateTimeOffset.UtcNow,
            kind, source, target, type, status, duration, failure));

    async Task IOutgoingGrainCallFilter.Invoke(IOutgoingGrainCallContext context)
    {
        if (!IsObserved(context.InterfaceMethod.DeclaringType!, context.InterfaceMethod.Name) || Scope() is not { } scope)
        {
            await context.Invoke();
            return;
        }
        var operation = Guid.NewGuid();
        var source = RequestContext.Get(SourceKey) as string;
        var target = ((IAddressable)context.Grain).GetGrainId().ToString();
        RequestContext.Set(ScopeKey, scope);
        RequestContext.Set(OperationKey, operation);
        if (Correlation() is { } correlation) { RequestContext.Set(CorrelationKey, correlation); }
        Record(scope, operation, ActivityKind.CallStarted, source, target,
            context.InterfaceMethod.Name, "started");
        var start = Stopwatch.GetTimestamp();
        try
        {
            await context.Invoke();
            Record(scope, operation, ActivityKind.CallCompleted, source, target,
                context.InterfaceMethod.Name, "completed", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception error)
        {
            Record(scope, operation, ActivityKind.CallFailed, source, target,
                context.InterfaceMethod.Name, "failed", Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                error.GetType().Name);
            throw;
        }
    }

    async Task IIncomingGrainCallFilter.Invoke(IIncomingGrainCallContext context)
    {
        var previousSource = RequestContext.Get(SourceKey);
        var target = ((IAddressable)context.Grain).GetGrainId().ToString();
        if (IsObserved(context.InterfaceMethod.DeclaringType!, context.InterfaceMethod.Name)
            && Scope() is { } scope)
        {
            Record(scope, Operation(), ActivityKind.CallArrived, previousSource as string, target,
                context.InterfaceMethod.Name, "arrived");
            RequestContext.Set(SourceKey, target);
        }
        try { await context.Invoke(); }
        finally
        {
            if (previousSource is null) { RequestContext.Remove(SourceKey); }
            else { RequestContext.Set(SourceKey, previousSource); }
        }
    }
}
