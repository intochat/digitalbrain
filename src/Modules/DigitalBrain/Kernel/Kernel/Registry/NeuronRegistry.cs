using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Registry;

public sealed record NeuronDescriptor(
    string Id,
    Type ContractType,
    string Name,
    string Description,
    bool AgentRoutable)
{
    public string ModuleId { get; init; } = "";
}

public interface INeuronRegistryContributor
{
    IReadOnlyList<NeuronDescriptor> Neurons { get; }
}

public interface INeuronRegistry
{
    IReadOnlyList<NeuronDescriptor> All { get; }
    NeuronDescriptor? Find(string id);
}

public sealed class NeuronRegistry : INeuronRegistry
{
    private readonly Dictionary<string, NeuronDescriptor> _byId;

    private NeuronRegistry(IEnumerable<NeuronDescriptor> descriptors)
    {
        _byId = new(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Id)
                || string.IsNullOrWhiteSpace(descriptor.Name)
                || string.IsNullOrWhiteSpace(descriptor.Description)
                || string.IsNullOrWhiteSpace(descriptor.ModuleId))
            { throw new ArgumentException("Neuron descriptors require an ID, name, description, and module ID."); }
            if (!typeof(INeuron).IsAssignableFrom(descriptor.ContractType) || !descriptor.ContractType.IsInterface)
            { throw new ArgumentException($"{descriptor.Id} must name an INeuron interface."); }
            if (!_byId.TryAdd(descriptor.Id, descriptor))
            { throw new InvalidOperationException($"Duplicate neuron registry ID '{descriptor.Id}'."); }
        }
        All = Array.AsReadOnly(_byId.Values.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<NeuronDescriptor> All { get; }
    public NeuronDescriptor? Find(string id) => _byId.GetValueOrDefault(id);

    public static NeuronRegistry FromModules(IEnumerable<ModuleDefinition> modules)
        => new(modules.SelectMany(module => Describe(module.CreateModule(), module.Id)));

    public static NeuronRegistry FromServices(IEnumerable<NeuronDescriptor> descriptors) => new(descriptors);

    public static void AddContributions(IServiceCollection services, IModule module)
    {
        foreach (var descriptor in Describe(module, module.GetType().FullName!))
        { services.AddSingleton(descriptor); }
    }

    private static IEnumerable<NeuronDescriptor> Describe(IModule module, string moduleId)
        => module is INeuronRegistryContributor contributor
            ? contributor.Neurons.Select(descriptor => descriptor with { ModuleId = moduleId })
            : [];
}
