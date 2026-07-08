namespace DigitalBrain.Core.Config;

public interface IPackConfigStore
{
    Task SetAsync(string scope, string pack, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAsync(string scope, string pack, CancellationToken cancellationToken = default);
}
