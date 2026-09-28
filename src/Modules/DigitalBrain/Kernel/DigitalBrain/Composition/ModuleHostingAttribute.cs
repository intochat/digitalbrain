namespace DigitalBrain.Core;

/// <summary>Names an optional hosting adapter without introducing a runtime dependency on the host.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleHostingAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
}