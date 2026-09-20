using DigitalBrain.Core;

namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseConfigurationContract() : ModuleConfigurationContract<ClickHouseModule, ClickHouseModuleOptions>(
    "Provider", "ConnectionName", "Hosting.Enabled", "Hosting.PersistentStorage", "Hosting.AlwaysRunInitScripts", "Hosting.Seeds")
{
    protected override ModuleDefinition Compile(ClickHouseModuleOptions options)
    {
        var settings = new Dictionary<string, string?>(ClickHouseModule.Define(options).Configuration)
        {
            ["DigitalBrain:ClickHouse:Hosting:Enabled"] = options.Hosting.Enabled.ToString(),
            ["DigitalBrain:ClickHouse:Hosting:PersistentStorage"] = options.Hosting.PersistentStorage.ToString(),
            ["DigitalBrain:ClickHouse:Hosting:AlwaysRunInitScripts"] = options.Hosting.AlwaysRunInitScripts.ToString(),
        };
        for (var i = 0; i < options.Hosting.Seeds.Count; i++) { settings[$"DigitalBrain:ClickHouse:Hosting:Seeds:{i}"] = options.Hosting.Seeds[i]; }
        return new(typeof(ClickHouseModule), settings);
    }
}

public static class ClickHouseModuleConfiguration
{
    public static ModuleConfiguration<ClickHouseModule> WithClickHouse(this ModuleConfiguration<ClickHouseModule> module,
        Action<ClickHouseResourceOptions>? configure = null)
    {
        module.ConfigureOptions<ClickHouseModuleOptions>(o =>
        {
            o.Provider = ClickHouseModule.DriverProviderName;
            o.Hosting.Enabled = true;
            configure?.Invoke(o.Hosting);
        }, "Provider", "Hosting.Enabled", "Hosting.PersistentStorage", "Hosting.AlwaysRunInitScripts", "Hosting.Seeds");
        return module;
    }
}
