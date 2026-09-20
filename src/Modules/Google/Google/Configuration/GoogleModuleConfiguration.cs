using DigitalBrain.Core;

namespace DigitalBrain.Google;

public sealed class GoogleConfigurationContract() : ModuleConfigurationContract<GoogleModule, GoogleModuleOptions>("PublicOrigin", "TokenEndpoint", "HostGmail")
{
    protected override ModuleDefinition Compile(GoogleModuleOptions options) => GoogleModule.Define(options);
}

public static class GoogleModuleConfiguration
{
    public static ModuleConfiguration<GoogleModule> WithGmail(this ModuleConfiguration<GoogleModule> module)
    {
        module.ConfigureOptions<GoogleModuleOptions>(o => o.HostGmail = true, "HostGmail");
        return module;
    }
    public static ModuleConfiguration<GoogleModule> WithTokenEndpoint(this ModuleConfiguration<GoogleModule> module, Uri endpoint)
    {
        module.ConfigureOptions<GoogleModuleOptions>(o => o.TokenEndpoint = endpoint, "TokenEndpoint");
        return module;
    }
    public static ModuleConfiguration<GoogleModule> WithOptions(this ModuleConfiguration<GoogleModule> module, GoogleModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}
