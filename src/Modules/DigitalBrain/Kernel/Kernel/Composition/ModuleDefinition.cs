using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Routing;
using DigitalBrain.Core.Registry;

namespace DigitalBrain.Core;

/// <summary>An immutable, transportable selection of a production module and its public settings.</summary>
public sealed class ModuleDefinition : IModule
{
    public ModuleDefinition(Type moduleType, IReadOnlyDictionary<string, string?>? configuration = null,
        IReadOnlyList<ModuleDefinition>? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(moduleType);
        if (!typeof(IModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract || moduleType.GetConstructor(Type.EmptyTypes) is null)
        { throw new ArgumentException("Select a concrete module with a public parameterless constructor.", nameof(moduleType)); }
        ModuleType = moduleType;
        Configuration = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(configuration ?? new Dictionary<string, string?>(), StringComparer.OrdinalIgnoreCase));
        Dependencies = Array.AsReadOnly((dependencies ?? []).ToArray());
    }

    public Type ModuleType { get; }
    public string Id => ModuleType.FullName!;
    public IReadOnlyDictionary<string, string?> Configuration { get; }
    public IReadOnlyList<ModuleDefinition> Dependencies { get; }
    public IModule CreateModule() => (IModule)Activator.CreateInstance(ModuleType)!;
    public void Configure(Orleans.Hosting.ISiloBuilder silo)
    {
        var module = CreateModule();
        NeuronRegistry.AddContributions(silo.Services, module);
        module.Configure(silo);
    }
    public void Configure(IEndpointRouteBuilder endpoints) => CreateModule().Configure(endpoints);
}
