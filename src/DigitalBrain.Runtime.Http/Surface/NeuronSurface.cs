using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;

namespace DigitalBrain.Runtime.Http.Surface;

/// <summary>
/// Description of a neuron instance.
/// </summary>
public sealed record NeuronInstance(string Contract, string Key, DateTimeOffset Created, string Creator);

/// <summary>
/// Provides neuron discovery and invocation surface over a runtime dispatcher.
/// </summary>
public sealed class NeuronSurface(IRuntimeInvocationDispatcher dispatcher, IReadOnlySet<string>? allowed = null)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public NeuronSurface Restrict(IReadOnlyCollection<string> names)
        => new(dispatcher, names.ToHashSet(StringComparer.Ordinal));

    public bool Allowed(string contract)
        => allowed is null || allowed.Contains(contract);

    public static Task<string> Get(string contract, string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var type = LoadedContracts.Find(contract);
        return Task.FromResult(PlainNeuronContracts.IdOf(type, key).ToString());
    }

    public async Task<JsonElement?> Call(string brainId, string contract, string key, string method, string arguments, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var type = LoadedContracts.Find(contract);
        JsonElement[] payload;
        try { payload = JsonSerializer.Deserialize<JsonElement[]>(arguments, Json) ?? []; }
        catch (JsonException error) { throw new ArgumentException("arguments must be a JSON array: " + error.Message, nameof(arguments)); }

        var target = type.GetMethods().Concat(type.GetInterfaces().SelectMany(inherited => inherited.GetMethods()))
            .Where(candidate => candidate.Name == method)
            .SingleOrDefault(candidate => candidate.GetParameters().Count(IsArgument) == payload.Length)
            ?? throw new ArgumentException($"{type.Name} has no method {method} taking {payload.Length} arguments.");

        var invocation = new RuntimeInvocation(
            Contract: contract,
            Key: key,
            Method: method,
            Arguments: payload,
            Behavior: string.Empty);

        return await dispatcher.DispatchAsync(brainId, invocation, ct).ConfigureAwait(false);
    }

    private static bool IsArgument(ParameterInfo parameter)
        => parameter.ParameterType != typeof(CancellationToken);
}
