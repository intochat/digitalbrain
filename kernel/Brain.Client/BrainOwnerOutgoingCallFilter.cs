using Brain.Contracts;
using Orleans.Runtime;

namespace Brain.Client;

public sealed class BrainOwnerOutgoingCallFilter(BrainOwnerContext ownerContext) : IOutgoingGrainCallFilter
{
    public async Task Invoke(IOutgoingGrainCallContext context)
    {
        var key = nameof(BrainOwnerId);
        var prior = RequestContext.Get(key);

        try
        {
            if (ownerContext.Current is { } owner)
                RequestContext.Set(key, owner);
            else
                RequestContext.Remove(key);

            await context.Invoke();
        }
        finally
        {
            if (prior is null)
                RequestContext.Remove(key);
            else
                RequestContext.Set(key, prior);
        }
    }
}
