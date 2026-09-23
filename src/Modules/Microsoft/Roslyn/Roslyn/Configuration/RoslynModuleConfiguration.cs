using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.Roslyn;

// Roslyn reads the shared DigitalBrain:Coding section owned by CodingModule; it declares no
// public members of its own, so it never re-emits those keys (which would collide with Coding's).
public sealed class RoslynConfigurationContract() : ModuleConfigurationContract<RoslynModule, RoslynModuleOptions>
{
    protected override ModuleDefinition Compile(RoslynModuleOptions options) => new(typeof(RoslynModule));
}