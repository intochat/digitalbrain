using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class IncomingReificationFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

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
                    await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        var delivery = CapabilityRequestContext.CurrentDelivery;

        if (delivery is null)
        {
            if (IsUnattributed(context.SourceId))
            {
                if (IsClientEntryPoint(contract))
                {
                    await InvokeClientEntryAsync(context, target).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                    return;
                }

                throw new NeuronAuthorizationException(
                    $"'{contract.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{target.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }

            throw new NeuronAuthorizationException(
                $"Semantic capability '{contract.DeclaringType!.FullName}.{contract.Name}' requires a committed capability request.");
        }

        if (CapabilityInvocation.IsEnumerationDispatch(context.InterfaceMethod))
        {
            await target.RecordStreamedCapabilityRequestAsync(delivery, context.SourceId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return;
        }

        var turn = await target.BeginIncomingCapabilityRequestAsync(delivery, context.SourceId).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        try
        {
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await target.CompleteIncomingCapabilityRequestAsync(turn).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch
        {
            await target.FailIncomingCapabilityRequestAsync(turn).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

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
                    await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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
            await context.Invoke().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private static bool IsClientEntryPoint(MethodInfo? method)
        => method?.DeclaringType?.GetCustomAttribute<ClientEntryPointAttribute>() is not null;

    private static bool IsUnattributed(GrainId? source)
        => source?.Key.ToString() is not { } key
            || key.IndexOf(IdentityPartSeparator, StringComparison.Ordinal) <= 0;

    private const char IdentityPartSeparator = '/';
}
