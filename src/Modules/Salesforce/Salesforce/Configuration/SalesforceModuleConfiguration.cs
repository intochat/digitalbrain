using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceConfigurationContract() : ModuleConfigurationContract<SalesforceModule, SalesforceModuleOptions>("McpEndpoint", "PublicOrigin", "HostMcp", "UseLocalMcp")
{
    protected override ModuleDefinition Compile(SalesforceModuleOptions options) => SalesforceModule.Define(options);
}

public static class SalesforceModuleConfiguration
{
    public static ModuleConfiguration<SalesforceModule> WithHostedMcp(this ModuleConfiguration<SalesforceModule> module, Uri? endpoint = null)
    {
        module.ConfigureOptions<SalesforceModuleOptions>(o => { o.HostMcp = true; o.McpEndpoint = endpoint; o.UseLocalMcp = false; }, "HostMcp", "McpEndpoint", "UseLocalMcp");
        return module;
    }
    public static ModuleConfiguration<SalesforceModule> WithLocalMcp(this ModuleConfiguration<SalesforceModule> module, Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || !endpoint.IsLoopback || endpoint.Scheme is not ("http" or "https"))
            { throw new ArgumentException("A local MCP endpoint must use loopback HTTP(S).", nameof(endpoint)); }
        module.ConfigureOptions<SalesforceModuleOptions>(o => { o.HostMcp = true; o.McpEndpoint = endpoint; o.UseLocalMcp = true; }, "HostMcp", "McpEndpoint", "UseLocalMcp");
        return module;
    }
    public static ModuleConfiguration<SalesforceModule> WithOptions(this ModuleConfiguration<SalesforceModule> module, SalesforceModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
}
