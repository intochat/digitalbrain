using DigitalBrain.Core;

namespace DigitalBrain.Qdrant;

public sealed class QdrantConfigurationContract() : ModuleConfigurationContract<QdrantModule, QdrantModuleOptions>(
    "Provider", "ConnectionName", "CollectionName", "VectorSize", "Hosting.Enabled", "Hosting.PersistentStorage")
{
    protected override ModuleDefinition Compile(QdrantModuleOptions options)
    {
        var settings = new Dictionary<string, string?>(QdrantModule.Define(options).Configuration)
        {
            ["DigitalBrain:Qdrant:Hosting:Enabled"] = options.Hosting.Enabled.ToString(),
            ["DigitalBrain:Qdrant:Hosting:PersistentStorage"] = options.Hosting.PersistentStorage.ToString(),
        };
        return new(typeof(QdrantModule), settings);
    }
}

public static class QdrantModuleConfiguration
{
    public static ModuleConfiguration<QdrantModule> WithQdrant(this ModuleConfiguration<QdrantModule> module)
    {
        module.ConfigureOptions<QdrantModuleOptions>(options =>
        {
            options.Provider = QdrantModule.ProviderName;
            options.Hosting.Enabled = true;
        }, "Provider", "Hosting.Enabled");
        return module;
    }
}
