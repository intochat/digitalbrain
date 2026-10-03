namespace DigitalBrain.Kernel;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleIdAttribute(string id) : Attribute { public string Id { get; } = id; }
public static class ModuleIdentity
{
    public static string Get(Type module) => module.GetCustomAttributes(typeof(ModuleIdAttribute), false)
        .Cast<ModuleIdAttribute>().SingleOrDefault()?.Id ?? module.Name;
}
