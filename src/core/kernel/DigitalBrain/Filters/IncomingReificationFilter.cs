using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class IncomingReificationFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.InterfaceMethod?.DeclaringType == typeof(ICapabilityDelegationAuthority))
        {
            if (context.Grain is not Neuron
                || context.Request.GetArgumentCount() == 0
                || context.Request.GetArgument(0) is not CapabilityDelegation delegation
                || delegation.DelegateSource != context.SourceId
                || delegation.Request.Caller.ToGrainId() != context.TargetId)
            {
                throw new NeuronAuthorizationException(
                    "The capability delegation authority callback does not match its actual runner and causal caller.");
            }

            await context.Invoke();

            return;
        }

        if (context.Grain is Neuron streamTarget
            && CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } streamEnumeration
            && streamTarget.TryGetClientStreamCorrelation(streamEnumeration, out var streamCorrelation)
            && CapabilityInvocation.ContractMethod(context.InterfaceMethod, context.Request) is null)
        {
            using (streamTarget.EnterClientEntryCorrelation(streamCorrelation))
            {
                try
                {
                    await context.Invoke();
                }
                finally
                {
                    if (CapabilityInvocation.IsEnumerationDisposal(context.InterfaceMethod)
                        || CapabilityInvocation.EnumerationTerminus(context.Result) is not null)
                    {
                        streamTarget.ForgetClientStreamCorrelation(streamEnumeration);
                    }
                }
            }

            return;
        }

        if (CapabilityInvocation.ContractMethod(context.InterfaceMethod, context.Request) is not { } contract
            || context.Grain is not Neuron target
            || (CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod)
                && !contract.DeclaringType!.IsInstanceOfType(target)))
        {
            await context.Invoke();

            return;
        }

        var direct = CapabilityRequestContext.CurrentDelivery;
        var redeemed = CapabilityRequestContext.CurrentRedeemedDelegation;

        if (direct is null && redeemed is null)
        {
            if (IsUnattributed(context.SourceId))
            {
                if (IsClientEntryPoint(contract))
                {
                    await InvokeClientEntryAsync(context, target);
                    return;
                }

                throw new NeuronAuthorizationException(
                    $"'{contract.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{target.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }

            throw new NeuronAuthorizationException(
                $"Semantic capability '{contract.DeclaringType!.FullName}.{contract.Name}' requires a committed capability request.");
        }

        SynapseDelivery delivery;
        GrainId? delegatedSource;

        if (redeemed is not null)
        {
            redeemed.Delegation.RequireMatches(context.SourceId, context.TargetId, context.InterfaceMethod);
            delivery = redeemed.Delegation.Request;
            delegatedSource = redeemed.Delegation.DelegateSource;
        }
        else
        {
            delivery = direct!;
            delegatedSource = null;
        }

        if (CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod))
        {
            await target.RecordStreamedCapabilityRequestAsync(delivery, context.SourceId, delegatedSource);
            await context.Invoke();

            return;
        }

        var turn = await target.BeginIncomingCapabilityRequestAsync(delivery, context.SourceId, delegatedSource);

        try
        {
            await context.Invoke();
            await target.CompleteIncomingCapabilityRequestAsync(turn);
        }
        catch
        {
            await target.FailIncomingCapabilityRequestAsync(turn);

            throw;
        }
    }

    private static async Task InvokeClientEntryAsync(IIncomingGrainCallContext context, Neuron target)
    {
        var correlation = CorrelationId.New();

        if (CapabilityInvocation.IsEnumerationStart(context.InterfaceMethod)
            && CapabilityInvocation.EnumerationId(context.Request) is { } enumerationId)
        {
            target.RegisterClientStreamCorrelation(enumerationId, correlation);
            using (target.EnterClientEntryCorrelation(correlation))
            {
                try
                {
                    await context.Invoke();
                }
                catch
                {
                    target.ForgetClientStreamCorrelation(enumerationId);
                    throw;
                }
                finally
                {
                    if (CapabilityInvocation.EnumerationTerminus(context.Result) is not null)
                    {
                        target.ForgetClientStreamCorrelation(enumerationId);
                    }
                }
            }

            return;
        }

        using (target.EnterClientEntryCorrelation(correlation))
        {
            await context.Invoke();
        }
    }

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;

    private static bool IsUnattributed(GrainId? source)
        => source?.Key.ToString() is not { } key
            || key.IndexOf(IdentityPartSeparator, StringComparison.Ordinal) <= 0;

    private const char IdentityPartSeparator = '/';
}
