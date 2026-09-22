using DigitalBrain.Core;

namespace DigitalBrain.Google.Gmail;

public sealed class GmailConfigurationContract() : ModuleConfigurationContract<GmailModule, GmailModuleOptions>("PublicOrigin", "TokenEndpoint", "HostGmail")
{
    protected override ModuleDefinition Compile(GmailModuleOptions options) => GmailModule.Define(options);
}

public static class GmailModuleConfiguration
{
    public static ModuleConfiguration<GmailModule> WithGmail(this ModuleConfiguration<GmailModule> module)
    {
        module.ConfigureOptions<GmailModuleOptions>(o => o.HostGmail = true, "HostGmail");
        return module;
    }
    public static ModuleConfiguration<GmailModule> WithTokenEndpoint(this ModuleConfiguration<GmailModule> module, Uri endpoint)
    {
        module.ConfigureOptions<GmailModuleOptions>(o => o.TokenEndpoint = endpoint, "TokenEndpoint");
        return module;
    }
    public static ModuleConfiguration<GmailModule> WithOptions(this ModuleConfiguration<GmailModule> module, GmailModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}