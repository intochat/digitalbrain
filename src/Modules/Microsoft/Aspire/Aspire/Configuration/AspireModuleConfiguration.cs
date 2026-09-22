using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.Aspire;

public sealed class AspireConfigurationContract() : ModuleConfigurationContract<AspireModule, AspireModuleOptions>(
    "ProjectPath", "ApplicationName")
{
    protected override ModuleDefinition Compile(AspireModuleOptions options) => AspireModule.Define(options);
}

public static class AspireModuleConfiguration
{
    public static ModuleConfiguration<AspireModule> WithAspire(this ModuleConfiguration<AspireModule> module, string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        module.ConfigureOptions<AspireModuleOptions>(o => o.ProjectPath = Path.GetFullPath(projectPath), "ProjectPath");
        return module;
    }

    public static ModuleConfiguration<AspireModule> WithoutAspire(this ModuleConfiguration<AspireModule> module)
    {
        module.ConfigureOptions<AspireModuleOptions>(o => o.ProjectPath = null, "ProjectPath");
        return module;
    }
}
