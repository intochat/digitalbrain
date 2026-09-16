using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

/// <summary>Dashboard-only parent for resources owned by a module's hosting integration.</summary>
public sealed class DigitalBrainModuleResource(string name, Type moduleType) : Resource(name)
{
    public Type ModuleType { get; } = moduleType;
}
