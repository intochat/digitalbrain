using DigitalBrain.Core;

namespace DigitalBrain.Behavior;

public sealed class BehaviorConfigurationContract() : ModuleConfigurationContract<BehaviorModule, BehaviorOptions>(
    "Root", "CodeRoot", "Gateways", "ClusterId", "ServiceId", "DotnetPath", "SandboxImage", "DockerPath", "StartupTimeout", "StopTimeout", "HeartbeatInterval", "HeartbeatLossTimeout", "MaximumLogBytes")
{
    protected override ModuleDefinition Compile(BehaviorOptions options) => BehaviorModule.Define(options);
}

public static class BehaviorModuleConfiguration
{
    public static ModuleConfiguration<BehaviorModule> WithLocalExecution(this ModuleConfiguration<BehaviorModule> module, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        module.ConfigureOptions<BehaviorOptions>(o =>
        {
            o.Root = Path.Combine(Path.GetFullPath(root), "behaviors");
            o.CodeRoot = Path.Combine(Path.GetFullPath(root), "coding");
        }, "Root", "CodeRoot");
        return module;
    }

    public static ModuleConfiguration<BehaviorModule> WithSandbox(this ModuleConfiguration<BehaviorModule> module)
    {
        module.ConfigureOptions<BehaviorOptions>(options => options.SandboxImage = BehaviorSandbox.ImageName, "SandboxImage");
        return module;
    }
}