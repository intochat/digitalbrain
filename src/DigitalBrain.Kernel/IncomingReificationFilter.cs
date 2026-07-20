using DigitalBrain.Abstractions;

namespace DigitalBrain.Kernel;

internal sealed class IncomingReificationFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CapabilityInvocation.IsRequest(context.InterfaceMethod)
            || CapabilityRequestContext.Current is not { } delivery
            || context.Grain is not Neuron target)
        {
            await context.Invoke();

            return;
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
}
