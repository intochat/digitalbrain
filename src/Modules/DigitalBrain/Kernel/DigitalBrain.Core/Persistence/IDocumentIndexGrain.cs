using Orleans;

namespace DigitalBrain.Core;

public interface IDocumentIndexGrain : IGrainWithStringKey
{
    Task<string[]> ListAsync();
    Task AddAsync(string id);
}
