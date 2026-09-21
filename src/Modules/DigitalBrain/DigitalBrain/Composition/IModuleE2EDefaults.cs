namespace DigitalBrain.Core;

/// <summary>Defaults for an explicitly selected module in an end-to-end test, before caller overrides.</summary>
public interface IModuleE2EDefaults<TModule> where TModule : class, IModule, new()
{
    void ConfigureE2E(ModuleConfiguration<TModule> module);
}
