using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Registry;
using System.Security.Cryptography;
using System.Text.Json;

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
    string Version { get; }
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
        Version = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(ToRegistrations(this))));
    }

    public IReadOnlyList<NeuronDescriptor> All { get; }
    public string Version { get; }
    public NeuronDescriptor? Find(string id) => _byId.GetValueOrDefault(id);

    public static NeuronRegistry FromModules(IEnumerable<ModuleDefinition> modules)
        => new(modules.SelectMany(module => Describe(module.CreateModule(), module.Id)));

    public static NeuronRegistry FromInstances(IEnumerable<IModule> modules)
        => new(modules.SelectMany(module => Describe(module, module.GetType().FullName!)));

    public static NeuronRegistration[] ToRegistrations(INeuronRegistry registry)
        => registry.All.Select(item => new NeuronRegistration
        {
            Id = item.Id,
            ContractType = item.ContractType.FullName!,
            ModuleId = item.ModuleId,
            Name = item.Name,
            Description = item.Description,
            AgentRoutable = item.AgentRoutable,
        }).ToArray();

    private static IEnumerable<NeuronDescriptor> Describe(IModule module, string moduleId)
        => module is INeuronRegistryContributor contributor
            ? contributor.Neurons.Select(descriptor => descriptor with { ModuleId = moduleId })
            : [];
}
