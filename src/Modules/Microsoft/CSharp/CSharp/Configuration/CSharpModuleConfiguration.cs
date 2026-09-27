using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpConfigurationContract() : ModuleConfigurationContract<CSharpModule, CSharpOptions>(
    "Root", "SourceRoot", "Image", "DockerPath", "Gateways", "GatewayRelayHost", "ClusterId", "ServiceId", "DockerTimeout")
{
    protected override ModuleDefinition Compile(CSharpOptions options) => CSharpModule.Define(options);
}

public static class CSharpModuleConfiguration
{
    public static ModuleConfiguration<CSharpModule> WithDocker(this ModuleConfiguration<CSharpModule> module, string root, string sourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        module.ConfigureOptions<CSharpOptions>(o =>
        {
            o.Root = Path.GetFullPath(root);
            o.SourceRoot = Path.GetFullPath(sourceRoot);
        }, "Root", "SourceRoot");
        return module;
    }
}
