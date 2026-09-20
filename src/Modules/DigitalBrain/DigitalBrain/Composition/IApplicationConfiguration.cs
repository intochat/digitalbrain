namespace DigitalBrain.Core;

public interface IApplicationConfiguration
{
    IReadOnlyList<ModuleDefinition> Modules { get; }
}
