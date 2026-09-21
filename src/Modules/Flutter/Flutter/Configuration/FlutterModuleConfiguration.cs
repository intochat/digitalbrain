using DigitalBrain.Core;

namespace DigitalBrain.Flutter;

public sealed class FlutterConfigurationContract() : ModuleConfigurationContract<FlutterModule, FlutterModuleOptions>(
    "Hosting.Kind", "Hosting.ResourceName", "Hosting.DeviceTarget", "Hosting.ShellName", "Hosting.ChatName",
    "Hosting.FlutterCommand", "Hosting.WorkingDirectory")
{
    protected override ModuleDefinition Compile(FlutterModuleOptions options) => FlutterModule.Define(options);
}

public static class FlutterModuleConfiguration
{
    public static ModuleConfiguration<FlutterModule> RunWebApp(this ModuleConfiguration<FlutterModule> module)
        => WithHost(module, FlutterHostKind.Web);
    public static ModuleConfiguration<FlutterModule> RunDesktopApp(this ModuleConfiguration<FlutterModule> module)
        => WithHost(module, FlutterHostKind.Window);
    public static ModuleConfiguration<FlutterModule> BackendOnly(this ModuleConfiguration<FlutterModule> module)
        => WithHost(module, FlutterHostKind.None);
    public static ModuleConfiguration<FlutterModule> WithOptions(this ModuleConfiguration<FlutterModule> module, FlutterModuleOptions options)
    {
        module.ReplaceOptions(options);
        return module;
    }
    private static ModuleConfiguration<FlutterModule> WithHost(ModuleConfiguration<FlutterModule> module, FlutterHostKind kind)
    {
        module.ConfigureOptions<FlutterModuleOptions>(options => options.Hosting = options.Hosting with { Kind = kind }, "Hosting.Kind");
        return module;
    }
}
