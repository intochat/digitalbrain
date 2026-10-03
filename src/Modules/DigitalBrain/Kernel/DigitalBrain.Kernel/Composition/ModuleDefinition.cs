using System.Collections.ObjectModel;

namespace DigitalBrain.Kernel;

public sealed class ModuleDefinition : IModule
{
    public ModuleDefinition(Type moduleType, IReadOnlyDictionary<string, string?>? configuration = null,
        IReadOnlyList<ModuleDefinition>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (!typeof(IModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract || moduleType.GetConstructor(Type.EmptyTypes) is null)
        { throw new ArgumentException("Select a concrete module with a public parameterless constructor.", nameof(moduleType)); }
        ModuleType = moduleType;
        _instance = new(() => (IModule)Activator.CreateInstance(moduleType)!);
        Configuration = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(configuration ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase));
        Dependencies = Array.AsReadOnly((dependencies ?? []).ToArray());
    }

    public Type ModuleType { get; }
    public string Id => ModuleType.FullName!;
    public IReadOnlyDictionary<string, string?> Configuration { get; }
    public IReadOnlyList<ModuleDefinition> Dependencies { get; }
    private readonly Lazy<IModule> _instance;
    public IModule CreateModule() => _instance.Value;
    public void Configure(Orleans.Hosting.ISiloBuilder silo) => CreateModule().Configure(silo);
}
