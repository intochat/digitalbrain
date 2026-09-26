using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Core.Registry;

namespace DigitalBrain.Core;

public sealed class BrainComposition
{
    private readonly Action<IServiceCollection>[] _localServices;
    internal BrainComposition(IReadOnlyList<ModuleDefinition> modules, Action<IServiceCollection>[] localServices, INeuronRegistry neuronRegistry)
    {
        Modules = modules;
        NeuronRegistry = neuronRegistry;
        _localServices = localServices;
    }
    public IReadOnlyList<ModuleDefinition> Modules { get; }
    public INeuronRegistry NeuronRegistry { get; }
    public bool RequiresLocalServices => _localServices.Length > 0;
    public void ConfigureLocalServices(IServiceCollection services)
    {
        foreach (var configure in _localServices) { configure(services); }
    }
}
