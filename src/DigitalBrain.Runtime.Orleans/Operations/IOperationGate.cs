using DigitalBrain.Operations;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>An operation about to run, as <see cref="IOperationGate"/> sees it.</summary>
/// <param name="Id">The operation id (its entity already exists).</param>
/// <param name="Brain">The brain it runs in.</param>
/// <param name="Origin">The neuron or app that started it.</param>
/// <param name="Plan">The plan.</param>
public sealed record OperationRequest(OperationId Id, BrainId Brain, string Origin, OperationPlan Plan);

/// <summary>What <see cref="IOperationGate"/> decided.</summary>
/// <param name="Allowed">True lets the operation run.</param>
/// <param name="Reason">Why it was refused; null when allowed.</param>
public sealed record OperationGateDecision(bool Allowed, string? Reason)
{
    /// <summary>Let it run.</summary>
    public static OperationGateDecision Allow { get; } = new(true, null);

    /// <summary>Refuse it; the operation ends <see cref="OperationResult.Failed"/> with code <c>denied</c>.</summary>
    public static OperationGateDecision Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new OperationGateDecision(false, reason);
    }
}

/// <summary>
/// Asked once before every operation runs (07 D-03 hook: Phase 7 puts the owner's confirmation here). The runtime registers
/// <see cref="ConfirmingOperationGate"/> (confirmations for storage writes) unless the host registers its own gate. Dry runs are asked too.
/// </summary>
public interface IOperationGate
{
    /// <summary>Allows or refuses <paramref name="request"/>.</summary>
    Task<OperationGateDecision> Check(OperationRequest request, CancellationToken ct);
}

/// <summary>The default gate: every operation runs.</summary>
public sealed class AllowAllOperations : IOperationGate
{
    /// <inheritdoc />
    public Task<OperationGateDecision> Check(OperationRequest request, CancellationToken ct) => Task.FromResult(OperationGateDecision.Allow);
}
