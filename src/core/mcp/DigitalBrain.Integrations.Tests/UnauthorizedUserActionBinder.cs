using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;

namespace DigitalBrain.Integrations.Tests;

[ClientEntryPoint]
[Alias(UnauthorizedUserActionBinder.NeuronContractId)]
[Description("Same-owner probe that attempts unauthorized first-bind of completion bridges and MCP targets")]
public partial interface IUnauthorizedUserActionBinder : INeuron
{
    [Alias(nameof(TryBindBridge))]
    Task TryBindBridge(BindUserActionCompletion bind, CancellationToken cancellationToken);

    [Alias(nameof(TryBindCompletionTarget))]
    Task TryBindCompletionTarget(
        BindMcpAuthorizationCompletionTarget request,
        CancellationToken cancellationToken);
}

[GrainType(UnauthorizedUserActionBinder.GrainTypeName)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the test silo from GrainType metadata.")]
internal sealed class UnauthorizedUserActionBinderNeuron :
    Neuron,
    IUnauthorizedUserActionBinder
{
    public async Task TryBindBridge(BindUserActionCompletion bind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        cancellationToken.ThrowIfCancellationRequested();

        var completer = UserActionCompletionBridge.For(Id.Owner, bind.ActionEpoch);
        // Direct Deliver so bridge authority is evaluated with this same-owner neuron as Caller.
        await GrainFactory.GetGrain<INeuron>(completer.ToGrainId()).Deliver(
            SynapseDelivery.Create(
                bind,
                Id,
                sequence: 1,
                cause: null,
                TimeProvider,
                CorrelationId.New()),
            cancellationToken);
    }

    public Task TryBindCompletionTarget(
        BindMcpAuthorizationCompletionTarget request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return GrainFactory
            .GetGrain<IMcpAuthorization>(
                NeuronId.For<IMcpAuthorization>(Id.Owner, McpAuthorizationNeuron.InstanceName).ToGrainId())
            .BindCompletionTarget(request, cancellationToken);
    }
}

public sealed partial class UnauthorizedUserActionBinderModule : IModule;

internal static class UnauthorizedUserActionBinder
{
    public const string NeuronContractId = "integrations.unauthorized-user-action-binder";
    public const string GrainTypeName = "unauthorizeduseractionbinder";
}
