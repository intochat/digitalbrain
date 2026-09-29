namespace DigitalBrain.Core;

// The modules selected for this host, available to modules without coupling hosting to their consumers.
public sealed class ModuleInventory
{
    public ModuleInventory(IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        Types = Array.AsReadOnly(moduleTypes.ToArray());
    }

    public IReadOnlyList<Type> Types { get; }
}