using DigitalBrain.Core;

namespace DigitalBrain.Connections;

// Connections exposes no public options of its own; the contract exists so the module
// participates in manifest composition like every other product module.
public sealed class ConnectionsModuleOptions
{
}

public sealed class ConnectionsConfigurationContract() : ModuleConfigurationContract<ConnectionsModule, ConnectionsModuleOptions>
{
    protected override ModuleDefinition Compile(ConnectionsModuleOptions options) => ConnectionsModule.Define();
}