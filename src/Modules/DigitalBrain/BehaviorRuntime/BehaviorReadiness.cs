using DigitalBrain.Contracts;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans;

namespace DigitalBrain.Core;

public sealed record SubscriptionRequirement(string Source, Type SignalType)
{
    public static SubscriptionRequirement For<T>(INeuron source) where T : Signal
        => new(source.GetGrainId().ToString(), typeof(T));
}

public readonly record struct BehaviorGeneration(string Name, Guid Id);

public sealed class BehaviorReadiness : IHealthCheck
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    public bool IsReady { get { lock (_gate) { return _states.Values.All(s => s.Active && s.Required.All(s.Ready.Contains)); } } }
    public BehaviorGeneration Begin(string name, IReadOnlyList<SubscriptionRequirement> requirements)
    {
        var generation = new BehaviorGeneration(name, Guid.NewGuid());
        lock (_gate)
        {
            if (_states.TryGetValue(name, out var previous)) { previous.Lost.TrySetResult(); }
            _states[name] = new(generation.Id, requirements.ToHashSet());
        }
        return generation;
    }
    public void SubscriptionReady(BehaviorGeneration generation, string source, Type signalType)
    { lock (_gate) { if (Current(generation) is { } state) { state.Ready.Add(new(source, signalType)); } } }
    public void SubscriptionClosed(BehaviorGeneration generation, string source, Type signalType)
    {
        lock (_gate)
        {
            if (Current(generation) is { } state)
            {
                var requirement = new SubscriptionRequirement(source, signalType);
                state.Ready.Remove(requirement);
                if (state.Required.Contains(requirement)) { state.Lost.TrySetResult(); }
            }
        }
    }
    public Task WaitForLossAsync(BehaviorGeneration generation)
    { lock (_gate) { return Current(generation)?.Lost.Task ?? Task.CompletedTask; } }
    public void End(BehaviorGeneration generation)
    { lock (_gate) { if (Current(generation) is { } state) { state.Active = false; state.Ready.Clear(); state.Lost.TrySetResult(); } } }
    private State? Current(BehaviorGeneration generation)
        => _states.TryGetValue(generation.Name, out var state) && state.Generation == generation.Id ? state : null;
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(IsReady ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Required behavior subscriptions are not ready."));
    private sealed class State(Guid generation, HashSet<SubscriptionRequirement> required)
    {
        public Guid Generation { get; } = generation;
        public HashSet<SubscriptionRequirement> Required { get; } = required;
        public HashSet<SubscriptionRequirement> Ready { get; } = [];
        public bool Active { get; set; } = true;
        public TaskCompletionSource Lost { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
