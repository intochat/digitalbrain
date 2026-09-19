namespace DigitalBrain.Aspire.Hosting;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleHostingAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
}
