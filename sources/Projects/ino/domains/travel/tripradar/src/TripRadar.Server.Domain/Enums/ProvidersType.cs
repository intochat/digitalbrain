using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class ProvidersType(int id, string name) : Enumeration(id, name)
{
    public static readonly ProvidersType SerpApi = new(5, nameof(SerpApi));

    public static Dictionary<string, bool> GetAvailableProviders() => new() {
            { SerpApi.Name, true }
        };
}
