using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using ModelContextProtocol.Server;
using Orleans;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class NeuronTools(IDigitalBrain brain)
{
    private static readonly NeuronContracts Contracts = new(NeuronContracts.Loaded());
    private static readonly HashSet<string> ObserverMethods = [nameof(INeuron.Watch), nameof(INeuron.Unwatch)];
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    private static readonly MethodInfo GetNeuron = typeof(IDigitalBrain).GetMethod(nameof(IDigitalBrain.Get))!;

    [McpServerTool(Name = "neurons_list"), Description("List neuron contracts this host can address. Pass the full type name to neurons_get and neurons_call.")]
    public IReadOnlyList<string> List() => [.. Contracts.Names.Order(StringComparer.Ordinal)];

    [McpServerTool(Name = "neurons_get"), Description("Resolve a neuron. contract is a full type name from neurons_list, key is the grain key, for example a timer name.")]
    public string Get(string contract, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Neuron(contract, key).GetGrainId().ToString();
    }

    [McpServerTool(Name = "neurons_call"), Description("Call a neuron method. arguments is a JSON array, one element per method argument, omitting CancellationToken. Watch and Unwatch are not callable.")]
    public async Task<JsonElement?> Call(string contract, string key, string method, string arguments = "[]", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var type = Contracts.Find(contract);
        var payload = JsonSerializer.Deserialize<JsonElement[]>(arguments, Json) ?? [];
        var target = type.GetMethods().Concat(type.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            .Where(candidate => candidate.Name == method && !ObserverMethods.Contains(candidate.Name))
            .SingleOrDefault(candidate => candidate.GetParameters().Count(IsArgument) == payload.Length)
            ?? throw new ArgumentException($"{type.Name} has no method {method} taking {payload.Length} arguments.");
        var parameters = target.GetParameters();
        var values = new object?[parameters.Length];
        for (int index = 0, argument = 0; index < parameters.Length; index++)
        {
            values[index] = IsArgument(parameters[index])
                ? payload[argument++].Deserialize(parameters[index].ParameterType, Json)
                : cancellationToken;
        }
        object? returned;
        try { returned = target.Invoke(Neuron(type, key), values); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
        if (returned is not Task task) { throw new NotSupportedException($"{type.Name}.{target.Name} does not return a Task."); }
        await task.ConfigureAwait(false);
        if (!target.ReturnType.IsGenericType) { return null; }
        var result = target.ReturnType.GetProperty(nameof(Task<object>.Result))!.GetValue(task);
        return JsonSerializer.SerializeToElement(result, target.ReturnType.GetGenericArguments()[0], Json);
    }

    private IAddressable Neuron(string contract, string key) => Neuron(Contracts.Find(contract), key);

    private IAddressable Neuron(Type contract, string key)
        => (IAddressable)GetNeuron.MakeGenericMethod(contract).Invoke(brain, [key])!;

    private static bool IsArgument(ParameterInfo parameter) => parameter.ParameterType != typeof(CancellationToken);
}
