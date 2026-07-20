using DigitalBrain.Abstractions;

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
            await context.Invoke();

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
            await caller.RecordCapabilityOutcomeAsync(
                CapabilityOutcome.Rejected,
                request);

            throw;
        }
        catch
        {
            await caller.RecordCapabilityOutcomeAsync(
                CapabilityOutcome.Failed,
                request);

            throw;
        }

        await caller.RecordCapabilityOutcomeAsync(
            CapabilityOutcome.Completed,
            request);
    }
}
