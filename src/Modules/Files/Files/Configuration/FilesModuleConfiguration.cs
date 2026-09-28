using DigitalBrain.Core;

namespace DigitalBrain.Files;

// Files reads its DigitalBrain:Files section at runtime; the contract lets the module take part in
// manifest composition like every other product module.
public sealed class FilesModuleOptions
{
}

public sealed class FilesConfigurationContract() : ModuleConfigurationContract<FilesModule, FilesModuleOptions>
{
    protected override ModuleDefinition Compile(FilesModuleOptions options) => FilesModule.Define();
}
