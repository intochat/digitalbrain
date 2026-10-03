using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Identity.Authority;

internal abstract class IdentityStateGrain<T>(IPersistentState<T> storage) : Grain where T : new()
{
    protected T State => storage.State;
    protected async Task Persist(T next)
    {
        var previous = storage.State;
        storage.State = next;
        try { await storage.WriteStateAsync(); }
        catch { storage.State = previous; DeactivateOnIdle(); throw; }
    }
}
