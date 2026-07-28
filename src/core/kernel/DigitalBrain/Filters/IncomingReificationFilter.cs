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

        if (CapabilityInvocation.ContractMethod(context.InterfaceMethod, context.Request) is not { } contract
            || context.Grain is not Neuron target
            || !contract.DeclaringType!.IsInstanceOfType(target))
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
                    await context.Invoke();

                    return;
                }

                throw new NeuronAuthorizationException(
                    $"'{contract.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{target.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }

            throw new NeuronAuthorizationException(
                $"Semantic capability '{contract.DeclaringType.FullName}.{contract.Name}' requires a committed capability request.");
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

        var turn = await target.BeginIncomingCapabilityRequestAsync(delivery, context.SourceId, delegatedSource);

        try
        {
            await context.Invoke();
            await target.CompleteIncomingCapabilityRequestAsync(turn);
        }
        catch
        {
            target.FailIncomingCapabilityRequest(turn);

            throw;
        }
    }

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;

    private static bool IsUnattributed(GrainId? source)
        => source?.Key.ToString() is not { } key
            || key.IndexOf(IdentityPartSeparator, StringComparison.Ordinal) <= 0;

    private const char IdentityPartSeparator = '/';
}
