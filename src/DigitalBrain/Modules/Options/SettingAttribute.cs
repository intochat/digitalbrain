using DigitalBrain.Neurons;

namespace DigitalBrain.Modules.Options;

/// <summary>
/// On a class: declares a non-secret integration setting (see <see cref="Integration"/>).
/// On a property: marks a module option member; <see cref="Required"/> is enforced when options are bound.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
public sealed class SettingAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
    public string? Integration { get; init; }
    public bool Optional { get; init; }
    public bool Required { get; init; }
}
