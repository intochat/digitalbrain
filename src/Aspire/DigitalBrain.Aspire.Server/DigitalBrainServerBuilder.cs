using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
namespace DigitalBrain.Aspire.Server;

public sealed class DigitalBrainServerBuilder
{
    private readonly Dictionary<string, Func<IModule>> _modules = new(StringComparer.Ordinal);
    internal bool AzureStorage { get; private set; }
    internal bool Dashboard { get; private set; }
    public DigitalBrainServerBuilder AddModule<TModule>(string id) where TModule : class, IModule, new()
        => AddModule(id, static () => new TModule());
    public DigitalBrainServerBuilder AddModule(string id, Func<IModule> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(create);
        if (!_modules.TryAdd(id, create)) { throw new InvalidOperationException($"Module '{id}' is already registered."); }
        return this;
    }
    public DigitalBrainServerBuilder UseAzureStorage() { AzureStorage = true; return this; }
    public DigitalBrainServerBuilder WithDashboard() { Dashboard = true; return this; }
    internal IModule[] SelectModules(IConfiguration configuration)
    {
        var ids = configuration.GetSection("DigitalBrain:Modules").GetChildren()
            .Where(section => section.GetValue<bool>("Enabled")).Select(section => section.Key).ToArray();
        foreach (var id in ids)
        {
            if (!_modules.ContainsKey(id))
            { throw new InvalidOperationException($"Selected module '{id}' is not registered in this server."); }
        }
        return ids.Select(id => _modules[id]() ?? throw new InvalidOperationException($"Factory for '{id}' returned null.")).ToArray();
    }
}
