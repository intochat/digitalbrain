using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Confirmation;
using DigitalBrain.Invocation;

namespace DigitalBrain.Operations;

/// <summary>Context passed to an operation handler describing the execution environment.</summary>
/// <param name="Brain">The brain the operation runs in.</param>
/// <param name="Origin">The neuron or caller that started the operation.</param>
/// <param name="Services">The service container for dependency resolution.</param>
/// <param name="Guard">Concurrency/freshness guard stamp recorded during approval describe, if any.</param>
/// <param name="Operations">Operations query interface for the brain.</param>
public sealed record OperationScope(
    BrainId Brain,
    string Origin,
    IServiceProvider Services,
    string? Guard,
    IOperationsQuery Operations);

/// <summary>Describes human approval required before an operation runs.</summary>
/// <param name="Question">The confirmation plan asking the human.</param>
/// <param name="Guard">Concurrency/freshness stamp to verify before executing after approval is granted.</param>
public sealed record OperationApproval(
    ConfirmationPlan Question,
    string? Guard = null);

/// <summary>
/// Inverted capability port: executes operations of plan type <typeparamref name="TPlan"/>.
/// </summary>
/// <typeparam name="TPlan">The concrete operation plan type.</typeparam>
public interface IOperationHandler<in TPlan> where TPlan : OperationPlan
{
    /// <summary>Describes whether this operation needs human approval before running.</summary>
    Task<OperationApproval?> Describe(TPlan plan, OperationScope scope, CancellationToken ct);

    /// <summary>Executes the operation, reporting progress and completing the operation neuron.</summary>
    Task<OperationResult> Run(TPlan plan, IOperationNeuron operation, OperationScope scope, CancellationToken ct);
}

/// <summary>Untyped operation execution port for dynamic dispatch.</summary>
public interface IOperationExecution
{
    /// <summary>Describes whether this operation needs human approval before running.</summary>
    Task<OperationApproval?> Describe(OperationPlan plan, OperationScope scope, CancellationToken ct);

    /// <summary>Executes the operation.</summary>
    Task<OperationResult> Run(OperationPlan plan, IOperationNeuron operation, OperationScope scope, CancellationToken ct);

    /// <summary>Deserializes the persisted JSON into the concrete operation plan.</summary>
    OperationPlan Deserialize(string planJson);
}

/// <summary>Registry of operation handlers keyed by plan type and plan kind name.</summary>
public sealed class OperationHandlers
{
    private readonly ConcurrentDictionary<Type, IOperationExecution> _byType = new();
    private readonly ConcurrentDictionary<string, IOperationExecution> _byKind = new(StringComparer.Ordinal);

    /// <summary>Registers a handler for a plan type.</summary>
    public OperationHandlers Register<TPlan>(IOperationHandler<TPlan> handler) where TPlan : OperationPlan
    {
        ArgumentNullException.ThrowIfNull(handler);
        var exec = new TypedExecution<TPlan>(handler);
        _byType[typeof(TPlan)] = exec;
        _byKind[typeof(TPlan).Name] = exec;
        return this;
    }

    /// <summary>Finds the execution adapter for the given plan type, or null if none registered.</summary>
    public IOperationExecution? Find(Type planType)
    {
        ArgumentNullException.ThrowIfNull(planType);
        return _byType.TryGetValue(planType, out var execution) ? execution : null;
    }

    /// <summary>Finds the execution adapter for the given plan kind name, or null if none registered.</summary>
    public IOperationExecution? Find(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _byKind.TryGetValue(kind, out var execution) ? execution : null;
    }

    private sealed class TypedExecution<TPlan>(IOperationHandler<TPlan> handler) : IOperationExecution
        where TPlan : OperationPlan
    {
        public Task<OperationApproval?> Describe(OperationPlan plan, OperationScope scope, CancellationToken ct) =>
            handler.Describe((TPlan)plan, scope, ct);

        public Task<OperationResult> Run(OperationPlan plan, IOperationNeuron operation, OperationScope scope, CancellationToken ct) =>
            handler.Run((TPlan)plan, operation, scope, ct);

        public OperationPlan Deserialize(string planJson) =>
            JsonSerializer.Deserialize<TPlan>(planJson, RuntimeProtocol.Json)!;
    }
}
