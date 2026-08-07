using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

internal sealed class OutgoingReificationFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceContext?.GrainInstance is Neuron streamingCaller
            && CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } enumerationId)
        {
            await InvokeStreamedAsync(context, streamingCaller, enumerationId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        if (!CapabilityInvocation.IsRequest(context.InterfaceMethod))
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        if (context.SourceContext?.GrainInstance is not Neuron
            && IsClientEntryPoint(context.InterfaceMethod))
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        var caller = context.SourceContext?.GrainInstance as Neuron
            ?? throw new NeuronAuthorizationException(
                $"Semantic capability '{context.InterfaceMethod!.DeclaringType!.FullName}.{context.InterfaceMethod.Name}' can be called only by a neuron with a committed capability request.");

        var interfaceName = context.InterfaceMethod!.DeclaringType!.FullName!;
        var methodName = context.InterfaceMethod.Name;
        var target = TargetOf(context);
        var request = await caller.BeginCapabilityRequestAsync(interfaceName, methodName, target).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        try
        {
            await CapabilityRequestContext.InvokeAsync(request, context.Invoke).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (NeuronAuthorizationException) when (caller.Id.Owner != target.Owner)
        {
            await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Rejected, request).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }
        catch
        {
            await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Failed, request).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }

        await caller.RecordCapabilityOutcomeAsync(CapabilityOutcome.Completed, request).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        var target = TargetOf(context);
        var request = await caller.BeginCapabilityRequestAsync(
            contract.DeclaringType!.FullName!,
            contract.Name,
            target).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!caller.TryRegisterStreamedCapabilityRequest(enumerationId, request))
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Abandoned).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            caller.TryRegisterStreamedCapabilityRequest(enumerationId, request);
        }

        try
        {
            await CapabilityRequestContext.InvokeAsync(request, context.Invoke).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (NeuronAuthorizationException) when (caller.Id.Owner != target.Owner)
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Rejected).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }
        catch
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Failed).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }

        await ClaimStreamedTerminusAsync(caller, enumerationId, context.Result).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static async Task ContinueStreamedRequestAsync(
        IOutgoingGrainCallContext context,
        Neuron caller,
        Guid enumerationId)
    {
        try
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Failed).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }

        await ClaimStreamedTerminusAsync(caller, enumerationId, context.Result).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static async Task AbandonStreamedRequestAsync(
        IOutgoingGrainCallContext context,
        Neuron caller,
        Guid enumerationId)
    {
        try
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            await ClaimAbandonmentWithoutMaskingAsync(caller, enumerationId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            throw;
        }

        await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Abandoned).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static async Task ClaimAbandonmentWithoutMaskingAsync(Neuron caller, Guid enumerationId)
    {
        try
        {
            await ClaimStreamedOutcomeAsync(caller, enumerationId, CapabilityOutcome.Abandoned).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            await caller.RecordCapabilityOutcomeAsync(outcome, request).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static NeuronId TargetOf(IOutgoingGrainCallContext context)
        => NeuronId.FromGrainKey(
            context.TargetId.Type.ToString()
                ?? throw new InvalidOperationException("The capability target has no grain type."),
            context.TargetId.Key.ToString());

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;
}
