using Orleans;

namespace DigitalBrain.Kernel;

public interface IDocumentIndexGrain : IGrainWithStringKey
{
    Task<string[]> ListAsync();
    Task AddAsync(string id);
}
