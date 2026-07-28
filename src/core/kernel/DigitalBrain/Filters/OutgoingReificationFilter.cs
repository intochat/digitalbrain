using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

internal sealed class OutgoingReificationFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceContext?.GrainInstance is Neuron streamingCaller
            && CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } enumerationId)
        {
            await InvokeStreamedAsync(context, streamingCaller, enumerationId);

            return;
        }

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
        var target = TargetOf(context);
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

    private static Task InvokeStreamedAsync(IOutgoingGrainCallContext context, Neuron caller, Guid enumerationId)
    {
        if (CapabilityInvocation.IsEnumerationStart(context.InterfaceMethod))
        {
            return StartStreamedRequestAsync(context, caller, enumerationId);
        }

        if (CapabilityInvocation.IsEnumerationContinuation(context.InterfaceMethod))
        {
            return ContinueStreamedRequestAsync(context, caller, enumerationId);
        }

        return CapabilityInvocation.IsEnumerationDisposal(context.InterfaceMethod)
            ? AbandonStreamedRequestAsync(context, caller, enumerationId)
            : context.Invoke();
    }

    private static async Task StartStreamedRequestAsync(
        IOutgoingGrainCallContext context,
        Neuron caller,
        Guid enumerationId)
    {
        if (CapabilityInvocation.ContractMethod(context.InterfaceMethod, context.Request) is not { } contract)
        {
            await context.Invoke();

            return;
        }

        var target = TargetOf(context);
        var request = await caller.BeginCapabilityRequestAsync(
            contract.DeclaringType!.FullName!,
            contract.Name,
            target);

        caller.RegisterStreamedCapabilityRequest(enumerationId, request);

        try
        {
            await CapabilityRequestContext.InvokeAsync(request, context.Invoke);
        }
        catch (NeuronAuthorizationException) when (caller.Id.Owner != target.Owner)
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Rejected);

            throw;
        }
        catch
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Failed);

            throw;
        }

        await ClaimStreamedTerminusAsync(caller, enumerationId, context.Result);
    }

    private static async Task ContinueStreamedRequestAsync(
        IOutgoingGrainCallContext context,
        Neuron caller,
        Guid enumerationId)
    {
        try
        {
            await context.Invoke();
        }
        catch
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Failed);

            throw;
        }

        await ClaimStreamedTerminusAsync(caller, enumerationId, context.Result);
    }

    private static async Task AbandonStreamedRequestAsync(
        IOutgoingGrainCallContext context,
        Neuron caller,
        Guid enumerationId)
    {
        try
        {
            await context.Invoke();
        }
        catch
        {
            await ClaimAbandonmentWithoutMaskingAsync(caller, enumerationId);

            throw;
        }

        await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Abandoned);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An abandonment journal failure must not replace the original disposal exception.")]
    private static async Task ClaimAbandonmentWithoutMaskingAsync(Neuron caller, Guid enumerationId)
    {
        try
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Abandoned);
        }
        catch
        {
        }
    }

    private static Task ClaimStreamedTerminusAsync(Neuron caller, Guid enumerationId, object? dispatchedResult)
        => CapabilityInvocation.EnumerationTerminus(dispatchedResult) is { } outcome
            ? ClaimStreamedOutcomeAsync(caller, enumerationId, outcome)
            : Task.CompletedTask;

    private static async Task ClaimStreamedOutcomeAsync(Neuron caller, Guid enumerationId, CapabilityOutcome outcome)
    {
        if (caller.TryClaimStreamedCapabilityRequest(enumerationId, out var request))
        {
            await caller.RecordCapabilityOutcomeAsync(outcome, request);
        }
    }

    private static NeuronId TargetOf(IOutgoingGrainCallContext context)
        => NeuronId.FromGrainKey(
            context.TargetId.Type.ToString()
                ?? throw new InvalidOperationException("The capability target has no grain type."),
            context.TargetId.Key.ToString());

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
