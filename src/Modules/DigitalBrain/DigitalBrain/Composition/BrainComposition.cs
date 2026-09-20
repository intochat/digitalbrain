namespace DigitalBrain.Core;

public sealed class BrainComposition
{
    internal BrainComposition(IReadOnlyList<ModuleDefinition> modules) => Modules = modules;
    public IReadOnlyList<ModuleDefinition> Modules { get; }
}
