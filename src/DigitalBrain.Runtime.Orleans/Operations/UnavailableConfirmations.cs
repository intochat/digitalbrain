using DigitalBrain.Confirmation;

namespace DigitalBrain.Runtime.Orleans.Operations;

// What Context.Confirmations is on a host that registered no IConfirmations (the runtime confirmations neuron is 07-06, os side).
internal sealed class UnavailableConfirmations : IConfirmations
{
    internal static readonly UnavailableConfirmations Instance = new();

    public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

    public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
        => throw new InvalidOperationException("This host registers no confirmation gate (IConfirmations).");

    public Task Resolve(string planId, bool approve, CancellationToken ct = default)
        => throw new InvalidOperationException("This host registers no confirmation gate (IConfirmations).");
}
