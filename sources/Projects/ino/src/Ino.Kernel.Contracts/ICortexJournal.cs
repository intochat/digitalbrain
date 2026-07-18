using Orleans;

namespace Ino.Kernel.Contracts;

public interface ICortexJournal : IGrainWithStringKey
{
    Task RecordAsync(string userId, RoutingDecision decision);
    Task<IReadOnlyList<RoutingDecision>> GetRecentAsync(string userId, int count);
}
