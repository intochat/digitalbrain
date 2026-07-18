using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK;

public interface INeuronExecutionContext
{
    IServiceProvider Services { get; }
    string BrainId { get; }
    Task EmitAsync(Synapse synapse, CancellationToken ct = default);
    Task<TResponse> AskAsync<TResponse>(string targetFqn, Synapse request, CancellationToken ct = default) where TResponse : Synapse;
    void Log(string message, params object[] args);
}

public sealed class NeuronBuilder
{
    private string _name = "DynamicNeuron";
    private readonly List<Type> _inputs = new();
    private readonly List<Type> _outputs = new();
    private readonly Dictionary<Type, Func<INeuronExecutionContext, Synapse, CancellationToken, Task>> _handlers = new();

    public NeuronBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public NeuronBuilder WithInputSynapse<TSynapse>() where TSynapse : Synapse
    {
        _inputs.Add(typeof(TSynapse));
        return this;
    }

    public NeuronBuilder WithOutputSynapse<TSynapse>() where TSynapse : Synapse
    {
        _outputs.Add(typeof(TSynapse));
        return this;
    }

    public NeuronBuilder OnReceive<TSynapse>(Func<INeuronExecutionContext, TSynapse, CancellationToken, Task> handler) where TSynapse : Synapse
    {
        _handlers[typeof(TSynapse)] = (ctx, syn, ct) => handler(ctx, (TSynapse)syn, ct);
        return this;
    }

    public ProgrammaticNeuron Build(IServiceProvider? services = null)
    {
        return new ProgrammaticNeuron(_name, _inputs, _outputs, _handlers, services);
    }
}

public sealed class ProgrammaticNeuron
{
    public string Name { get; }
    public IReadOnlyList<Type> InputSynapses { get; }
    public IReadOnlyList<Type> OutputSynapses { get; }
    private readonly Dictionary<Type, Func<INeuronExecutionContext, Synapse, CancellationToken, Task>> _handlers;
    private readonly IServiceProvider? _services;

    internal ProgrammaticNeuron(
        string name,
        List<Type> inputs,
        List<Type> outputs,
        Dictionary<Type, Func<INeuronExecutionContext, Synapse, CancellationToken, Task>> handlers,
        IServiceProvider? services)
    {
        Name = name;
        InputSynapses = inputs;
        OutputSynapses = outputs;
        _handlers = handlers;
        _services = services;
    }

    public async Task ExecuteAsync(Synapse synapse, INeuronExecutionContext context, CancellationToken ct = default)
    {
        var type = synapse.GetType();
        if (_handlers.TryGetValue(type, out var handler))
        {
            await handler(context, synapse, ct);
        }
        else
        {
            throw new InvalidOperationException($"No handler registered for synapse type {type.FullName} on programmatic neuron {Name}.");
        }
    }
}
