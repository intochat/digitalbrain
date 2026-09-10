using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Core;

internal sealed class OutgoingCallerFilter : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        if (context.SourceContext?.GrainInstance is not Neuron || context.SourceId is not { } sourceId)
        {
            await context.Invoke().ConfigureAwait(true);
            return;
        }

        var previous = RequestContext.Get(CallerContext.Caller);
        RequestContext.Set(CallerContext.Caller, NeuronId.FromGrainId(sourceId).ToString());
        try
        {
            await context.Invoke().ConfigureAwait(true);
        }
        finally
        {
            if (previous is null)
            {
                RequestContext.Remove(CallerContext.Caller);
            }
            else
            {
                RequestContext.Set(CallerContext.Caller, previous);
            }
        }
    }
}
