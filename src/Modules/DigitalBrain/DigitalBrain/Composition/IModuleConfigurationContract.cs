namespace DigitalBrain.Core;

/// <summary>Module-owned validation and public configuration transport.</summary>
public interface IModuleConfigurationContract
{
    Type ModuleType { get; }
    Type OptionsType { get; }
    object CreateDefaults();
    object Copy(object options);
    ModuleDefinition Compile(object options);
    object ApplyOverride(object baseline, string json);
    string WriteOverride(object configured, IReadOnlyCollection<string> assignedMembers);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleConfigurationAttribute(Type contractType) : Attribute
{
    public Type ContractType { get; } = contractType;
}
