using System.Reflection;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Client;

public sealed class DigitalBrainClient
{
    private const string SessionName = "session";
    private const string DefaultInstance = "default";

    private readonly IGrainFactory _grains;
    private readonly Dictionary<Type, Func<Synapse, Task>> _handlers = [];

    private DigitalBrainClient(IGrainFactory grains, OwnerId owner)
    {
        _grains = grains;
        Owner = owner;
    }

    public OwnerId Owner { get; }

    public static DigitalBrainClient Connect(IGrainFactory grains, string owner)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        return new DigitalBrainClient(grains, new OwnerId(owner));
    }

    public T Get<T>()
        where T : class, IGrainWithStringKey
        => Get<T>(DefaultInstance);

    public T Get<T>(string name)
        where T : class, IGrainWithStringKey
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var grainType = GrainTypeFor(typeof(T));
        var id = new NeuronId(grainType, Owner, name);

        return _grains.GetGrain<T>(id.ToGrainId());
    }

    public Task Emit(Synapse fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        return Session().EmitAsync(fact);
    }

    public Task On<TSynapse>(Func<TSynapse, Task> handler)
        where TSynapse : Synapse
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[typeof(TSynapse)] = synapse => handler((TSynapse)synapse);

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<Type> HandledSynapseTypes() => _handlers.Keys.ToArray();

    private ISessionNeuron Session()
        => _grains.GetGrain<ISessionNeuron>(
            new NeuronId(ISessionNeuron.GrainTypeName, Owner, SessionName).ToGrainId());

    private static string GrainTypeFor(Type contract)
    {
        var declared = contract.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.Name == "GrainTypeAttribute")?
            .ConstructorArguments is { Count: > 0 } args
            ? args[0].Value as string
            : null;

        if (declared is not null)
        {
            return declared;
        }

        var name = contract.Name;

        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name[1..];
        }

        return name;
    }
}
