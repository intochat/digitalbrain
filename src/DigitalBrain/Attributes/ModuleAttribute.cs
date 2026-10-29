namespace DigitalBrain;

/// <summary>Marks a module class. The id is frozen.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleAttribute(string id) : Attribute
{
    /// <summary>The frozen module id.</summary>
    public string Id { get; } = id;
}
