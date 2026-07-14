namespace DigitalBrain.Kernel.Contracts.Configuration;

public interface IIntegrationConfigStore
{
    Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack, CancellationToken cancellationToken = default);
}
