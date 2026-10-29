using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Runtime.Orleans.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>
/// The default <see cref="IOperationGate"/> (07 D-03): a storage <see cref="WritePlan"/> is put to the host's
/// <see cref="IConfirmations"/> under the brain policy (by default delete, move between connections and overwrite ask), and runs
/// only when approved. Storage writes fail closed without a registered confirmation service; dry runs and other plans do not ask.
/// </summary>
/// <param name="services">The silo services.</param>
public sealed class ConfirmingOperationGate(IServiceProvider services) : IOperationGate
{
    /// <inheritdoc />
    public async Task<OperationGateDecision> Check(OperationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Plan is not WritePlan write || write.DryRun) { return OperationGateDecision.Allow; }
        var confirmations = services.GetService<IConfirmationContexts>()?.For(request.Brain, request.Origin)
            ?? services.GetService<IConfirmations>();
        if (confirmations is null) { return OperationGateDecision.Deny("The host has no confirmation service."); }
        var broker = services.GetService<CapabilityBroker>()?.ForApp(request.Origin);
        if (broker is null) { return OperationGateDecision.Deny("The host has no storage broker."); }
        var refusal = await new StorageOperationGate(broker, confirmations, request.Brain).Ask(write, ct).ConfigureAwait(false);
        return refusal is null ? OperationGateDecision.Allow : OperationGateDecision.Deny(refusal.Value.Reason);
    }
}
