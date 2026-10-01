using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

public sealed class ModuleConfiguration<TModule> where TModule : class, IModule, new()
{
    private readonly ModuleDraft _draft;
    private readonly Action _ensureMutable;
    internal ModuleConfiguration(ModuleDraft draft, Action ensureMutable) { _draft = draft; _ensureMutable = ensureMutable; }

    public void ConfigureLocalServices(Action<IServiceCollection> configure)
    {
        _ensureMutable();
        ArgumentNullException.ThrowIfNull(configure);
        _draft.LocalServices.Add(configure);
    }
}
