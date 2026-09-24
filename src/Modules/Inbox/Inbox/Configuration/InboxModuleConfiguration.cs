using DigitalBrain.Core;

namespace DigitalBrain.Inbox;

// The inbox exposes no public settings; the contract exists so the module composes like every other product module.
public sealed class InboxModuleOptions
{
}

public sealed class InboxConfigurationContract() : ModuleConfigurationContract<InboxModule, InboxModuleOptions>
{
    protected override ModuleDefinition Compile(InboxModuleOptions options) => InboxModule.Define();
}
