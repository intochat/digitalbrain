using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

internal sealed class ModuleDraft
{
    public Type Type { get; }
    public ModuleOptionsSlot? Options { get; private init; }
    public List<Action<IServiceCollection>> LocalServices { get; } = [];

    public ModuleDraft(Type type)
    {
        Type = type;
        Options = ModuleOptionsSlot.For(type);
    }

    public ModuleDefinition Compile() => Options?.Compile() ?? new(Type);
    public ModuleDraft Copy()
    {
        var copy = new ModuleDraft(Type) { Options = Options?.Clone() };
        copy.LocalServices.AddRange(LocalServices);
        return copy;
    }
}
