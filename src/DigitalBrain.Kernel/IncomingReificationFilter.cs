using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class IncomingReificationFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CapabilityInvocation.IsRequest(context.InterfaceMethod)
            || context.Grain is not Neuron target)
        {
            await context.Invoke();

            return;
        }

        if (CapabilityRequestContext.Current is not { } delivery)
        {
            if (IsUnattributed(context.SourceId))
            {
                if (IsClientEntryPoint(context.InterfaceMethod))
                {
                    await context.Invoke();

                    return;
                }

                throw new NeuronAuthorizationException(
                    $"'{context.InterfaceMethod?.Name}' is not a client entry point, so an unattributed caller cannot be authorized to reach '{target.Id}'. Reach a neuron through a session of the owner you are acting as.");
            }

            throw new NeuronAuthorizationException(
                $"Semantic capability '{context.InterfaceMethod!.DeclaringType!.FullName}.{context.InterfaceMethod.Name}' requires a committed capability request.");
        }

        var turn = await target.BeginIncomingCapabilityRequestAsync(delivery, context.SourceId);

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
