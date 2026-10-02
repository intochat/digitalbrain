namespace DigitalBrain.Core;

// Names a module's cloud deployment contributor without the module depending on the deployment stack.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleDeploymentAttribute(string typeName) : Attribute
{
    public string TypeName { get; } = typeName;
}
