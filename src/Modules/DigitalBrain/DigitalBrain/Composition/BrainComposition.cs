using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

public sealed class BrainComposition
{
    private readonly Action<IServiceCollection>[] _localServices;
    internal BrainComposition(IReadOnlyList<ModuleDefinition> modules, Action<IServiceCollection>[] localServices)
    {
        Modules = modules;
        _localServices = localServices;
    }
    public IReadOnlyList<ModuleDefinition> Modules { get; }
    public bool RequiresLocalServices => _localServices.Length > 0;
    public void ConfigureLocalServices(IServiceCollection services)
    {
        foreach (var configure in _localServices) { configure(services); }
    }
}