namespace DigitalBrain.Abstractions;

/// <summary>Names an optional AppHost configurator without taking a runtime dependency on Aspire.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleHostingAttribute(string typeName) : Attribute
{
    /// <summary>The assembly-qualified type implementing the AppHost hosting interface.</summary>
    public string TypeName { get; } = typeName;
}
