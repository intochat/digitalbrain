using DigitalBrain.Core;

namespace DigitalBrain.Memory;

public sealed class MemoryConfigurationContract() : ModuleConfigurationContract<MemoryModule, MemoryModuleOptions>("CollectionName")
{
    protected override ModuleDefinition Compile(MemoryModuleOptions options) => MemoryModule.Define(options);
}

public static class MemoryModuleConfiguration
{
    public static ModuleConfiguration<MemoryModule> WithOptions(this ModuleConfiguration<MemoryModule> module, MemoryModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}
