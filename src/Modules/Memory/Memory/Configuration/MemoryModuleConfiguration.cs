using DigitalBrain.Core;

namespace DigitalBrain.Memory;

public sealed class MemoryConfigurationContract() : ModuleConfigurationContract<MemoryModule, MemoryModuleOptions>(
    "Provider", "Qdrant.ConnectionName", "Qdrant.CollectionName", "HostQdrant")
{
    protected override ModuleDefinition Compile(MemoryModuleOptions options) => MemoryModule.Define(options);
}

public static class MemoryModuleConfiguration
{
    public static ModuleConfiguration<MemoryModule> WithQdrant(this ModuleConfiguration<MemoryModule> module, string? collectionName = null)
    {
        module.ConfigureOptions<MemoryModuleOptions>(o =>
        {
            o.Provider = MemoryModule.QdrantProviderName;
            o.HostQdrant = true;
            o.Qdrant.CollectionName = collectionName;
        }, "Provider", "HostQdrant", "Qdrant.CollectionName");
        return module;
    }
    public static ModuleConfiguration<MemoryModule> WithOptions(this ModuleConfiguration<MemoryModule> module, MemoryModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}
