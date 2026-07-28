using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

internal sealed class OutgoingReificationFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CapabilityInvocation.IsRequest(context.InterfaceMethod))
        {
            await context.Invoke();

            return;
        }

        var caller = context.SourceContext?.GrainInstance as Neuron;

        if (caller is null)
        {
            await InvokeDelegatedAsync(context);

            return;
        }

        var interfaceName = context.InterfaceMethod!.DeclaringType!.FullName!;
        var methodName = context.InterfaceMethod.Name;
        var target = NeuronId.FromGrainKey(
            context.TargetId.Type.ToString()
                ?? throw new InvalidOperationException("The capability target has no grain type."),
            context.TargetId.Key.ToString());
        var request = await caller.BeginCapabilityRequestAsync(interfaceName, methodName, target);

        try
        {
            await CapabilityRequestContext.InvokeAsync(request, context.Invoke);
        }
        catch (NeuronAuthorizationException) when (caller.Id.Owner != target.Owner)
        {
            await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Rejected, request);

            throw;
        }
        catch
        {
            await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Failed, request);

            throw;
        }

        await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Completed, request);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "FinishAsync failures must not replace the original semantic exception.")]
    private static async Task InvokeDelegatedAsync(IOutgoingGrainCallContext context)
    {
        var delegation = CapabilityRequestContext.CurrentDelegation
            ?? throw new NeuronAuthorizationException(
                $"Semantic capability '{context.InterfaceMethod!.DeclaringType!.FullName}.{context.InterfaceMethod.Name}' can be called only by a neuron with a committed capability request or its authorized delegate runner.");

        if (context.SourceId != context.SourceContext?.GrainId)
        {
            throw new NeuronAuthorizationException(
                "The delegated call's inherited Orleans source does not match its activation source.");
        }

        delegation.RequireMatches(context.SourceId, context.TargetId, context.InterfaceMethod);

        var authority = context.SourceContext!.ActivationServices
            .GetRequiredService<IGrainFactory>()
            .GetGrain<ICapabilityDelegationAuthority>(delegation.Request.Caller.ToGrainId());

        await authority.RedeemAsync(delegation);

        try
        {
            await CapabilityRequestContext.InvokeRedeemedAsync(delegation, context.Invoke);
        }
        catch
        {
            try
            {
                await authority.FinishAsync(delegation, succeeded: false);
            }
            catch
            {
            }

            throw;
        }

        await authority.FinishAsync(delegation, succeeded: true);
    }
}
