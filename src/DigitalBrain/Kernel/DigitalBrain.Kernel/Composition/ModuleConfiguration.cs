using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Kernel;

public sealed class ModuleConfiguration<TModule> where TModule : class, IModule, new()
{
    private readonly ModuleDraft _draft;
    internal ModuleConfiguration(ModuleDraft draft) => _draft = draft;

    public void ConfigureLocalServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _draft.LocalServices.Add(configure);
    }
}
